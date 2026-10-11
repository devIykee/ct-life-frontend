using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    // Candidate: called after Animator evaluation only during an inspected Cafe exit's walk->idle blend.
    // It does not own root, hips, clip time, controller IK settings, or ordinary locomotion.
    public sealed class CafeExitSettleContact
    {
        private sealed class Skin
        {
            internal Transform[] bones;
            internal Matrix4x4[] matrices;
            internal Vertex[][] feet;
        }
        private struct Vertex
        {
            internal BoneWeight weight;
            internal Vector3 p0,p1,p2,p3;
        }
        private readonly Animator _animator;
        private readonly Skin[] _skins;
        private readonly Transform[] _hips=new Transform[2],_knees=new Transform[2],_feet=new Transform[2];
        private readonly Vector3[] _lastBend=new Vector3[2];
        private static readonly int Walk=Animator.StringToHash("loco_walk"), Idle=Animator.StringToHash("loco_idle");
        public const float MaximumLift=.015f;
        public int ShoeVertexCount { get; private set; }
        public int MatrixCount { get; private set; }
        public int SolveCount { get; private set; }
        public float MaximumAnkleLift { get; private set; }
        public float MaximumTargetError { get; private set; }
        public string Failure { get; private set; }
        public bool Active { get; private set; }
        private float _floor;

        public CafeExitSettleContact(Animator animator,SkinnedMeshRenderer[] renderers)
        {
            if(animator==null||!animator.isHuman||renderers==null||renderers.Length!=3)throw new ArgumentException("Exact three-skin Humanoid required.");
            _animator=animator;
            for(int side=0;side<2;side++)
            {
                _hips[side]=animator.GetBoneTransform(side==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
                _knees[side]=animator.GetBoneTransform(side==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
                _feet[side]=animator.GetBoneTransform(side==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                if(_hips[side]==null||_knees[side]==null||_feet[side]==null)throw new ArgumentException("Complete leg chains required.");
            }
            var skins=new List<Skin>();
            foreach(var renderer in renderers)
            {
                var mesh=renderer.sharedMesh;if(mesh==null)throw new ArgumentException("Actual skinned mesh required.");var bones=renderer.bones;var weights=mesh.boneWeights;var vertices=mesh.vertices;var bind=mesh.bindposes;
                if(mesh==null||weights.Length!=vertices.Length||bind.Length!=bones.Length)throw new ArgumentException("Expected fixed linear-skinned mesh.");
                for(int i=0;i<mesh.blendShapeCount;i++)if(Mathf.Abs(renderer.GetBlendShapeWeight(i))>.0001f)throw new ArgumentException("Nonzero blendshape needs separate sole evaluation proof.");
                var skin=new Skin{bones=bones,matrices=new Matrix4x4[bones.Length],feet=new Vertex[2][]};
                for(int side=0;side<2;side++)
                {
                    var selected=new List<Vertex>();
                    for(int i=0;i<vertices.Length;i++)
                    {
                        var w=weights[i];
                        float W(int b,float value)=>value>0&&(bones[b]==_feet[side]||bones[b].IsChildOf(_feet[side]))?value:0;
                        if(W(w.boneIndex0,w.weight0)+W(w.boneIndex1,w.weight1)+W(w.boneIndex2,w.weight2)+W(w.boneIndex3,w.weight3)<=.5f)continue;
                        selected.Add(new Vertex{weight=w,p0=bind[w.boneIndex0].MultiplyPoint3x4(vertices[i]),p1=bind[w.boneIndex1].MultiplyPoint3x4(vertices[i]),p2=bind[w.boneIndex2].MultiplyPoint3x4(vertices[i]),p3=bind[w.boneIndex3].MultiplyPoint3x4(vertices[i])});
                    }
                    if(selected.Count<3)throw new ArgumentException("Actual shoe surface vertices required.");
                    skin.feet[side]=selected.ToArray();ShoeVertexCount+=selected.Count;
                }
                skins.Add(skin);MatrixCount+=bones.Length;
            }
            _skins=skins.ToArray();
        }
        public void Begin(float floorY){_floor=floorY;Active=true;Failure=null;SolveCount=0;MaximumAnkleLift=MaximumTargetError=0;}
        public void End(){Active=false;}
        public float MinimumSole(int side)
        {
            float result=float.PositiveInfinity;
            foreach(var skin in _skins)
            {
                for(int i=0;i<skin.bones.Length;i++)skin.matrices[i]=skin.bones[i].localToWorldMatrix;
                foreach(var v in skin.feet[side])
                {
                    var w=v.weight;
                    var point=skin.matrices[w.boneIndex0].MultiplyPoint3x4(v.p0)*w.weight0+skin.matrices[w.boneIndex1].MultiplyPoint3x4(v.p1)*w.weight1+
                        skin.matrices[w.boneIndex2].MultiplyPoint3x4(v.p2)*w.weight2+skin.matrices[w.boneIndex3].MultiplyPoint3x4(v.p3)*w.weight3;
                    result=Mathf.Min(result,point.y);
                }
            }
            return result;
        }
        public void Apply()
        {
            if(!Active)return;
            var current=_animator.GetCurrentAnimatorStateInfo(0);
            if(!_animator.IsInTransition(0)||current.shortNameHash!=Walk||_animator.GetNextAnimatorStateInfo(0).shortNameHash!=Idle){End();return;}
            for(int side=0;side<2;side++)
            {
                float total=0;
                for(int iteration=0;iteration<2;iteration++)
                {
                    float minimum=MinimumSole(side);
                    if(minimum>=_floor-.00005f)break;
                    float lift=_floor+.00025f-minimum;
                    if(!float.IsFinite(lift)||total+lift>MaximumLift){Failure="Required ankle lift exceeds bounded15mm correction.";return;}
                    var target=_feet[side].position+Vector3.up*lift;
                    if(!Solve(side,target)){Failure="Foot target is outside current leg reach or bend plane is undefined.";return;}
                    total+=lift;SolveCount++;MaximumAnkleLift=Mathf.Max(MaximumAnkleLift,total);
                    MaximumTargetError=Mathf.Max(MaximumTargetError,Vector3.Distance(_feet[side].position,target));
                }
            }
        }
        private bool Solve(int side,Vector3 target)
        {
            var hip=_hips[side];var knee=_knees[side];var foot=_feet[side];var a=hip.position;var b=knee.position;var c=foot.position;
            float upper=Vector3.Distance(a,b),lower=Vector3.Distance(b,c),distance=Vector3.Distance(a,target);
            if(distance<.00001f||distance>=upper+lower-.00001f||distance<=Mathf.Abs(upper-lower)+.00001f)return false;
            var axis=(target-a)/distance;var bend=b-a-axis*Vector3.Dot(b-a,axis);
            if(bend.sqrMagnitude<1e-10f)bend=_lastBend[side]-axis*Vector3.Dot(_lastBend[side],axis);
            if(bend.sqrMagnitude<1e-10f)return false;
            bend.Normalize();_lastBend[side]=bend;
            float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
            var nextKnee=a+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            var orientation=foot.rotation;
            hip.rotation=Quaternion.FromToRotation(b-a,nextKnee-a)*hip.rotation;
            knee.rotation=Quaternion.FromToRotation(foot.position-knee.position,target-knee.position)*knee.rotation;
            foot.rotation=orientation;
            return true;
        }
    }
}
