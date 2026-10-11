using System;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Exact Snow/source binding. Candidate data is not installed in Resources until reviewed.</summary>
    [CreateAssetMenu(menuName="CT Life/Snow Run Correction")]
    public sealed class SnowRunCorrectionProfile : ScriptableObject
    {
        public const string ResourcePath="Characters/SnowRunCorrection";
        public const string AcceptedTableSha256="b5dafc026ff7dc27a8c05f16fee4f19889834a44e1300ce1b02010a5aad4835b";
        public GameObject sourcePrefab;
        public Avatar sourceAvatar;
        public AnimationClip sourceClip;
        public RuntimeAnimatorController[] allowedControllers;
        public TextAsset sourceTable;
        private Quaternion[][] _deltas;
        private bool _read;
        private string _failure;
        internal static readonly HumanBodyBones[] Bones={HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot};
        public bool Matches(Animator animator,GameObject prefab)
        {
            if(animator==null||prefab==null||prefab!=sourcePrefab||animator.avatar!=sourceAvatar||!animator.isHuman||
                sourceClip==null||sourceTable==null||animator.applyRootMotion||allowedControllers==null)return false;
            if(!Allows(animator.runtimeAnimatorController))return false;
            return Read(out _);
        }
        internal bool Allows(RuntimeAnimatorController controller)
        {
            if(controller==null||allowedControllers==null)return false;
            foreach(var allowed in allowedControllers)if(controller==allowed)return true;
            return false;
        }
        public bool Read(out string failure)
        {
            if(_read){failure=_failure;return _deltas!=null;}_read=true;
            try
            {
                if(sourceTable==null||sourceClip==null)throw new InvalidOperationException("Source table or clip missing.");
                using(var sha=SHA256.Create())
                {
                    string actual=BitConverter.ToString(sha.ComputeHash(sourceTable.bytes)).Replace("-","").ToLowerInvariant();
                    if(actual!=AcceptedTableSha256)throw new InvalidOperationException("Unaccepted local rotation table.");
                }
                var table=JObject.Parse(sourceTable.text);
                var rows=table["deltas"] as JArray;var names=table["bones"] as JArray;
                if((int?)table["keys"]!=480||rows==null||rows.Count!=481||names==null||names.Count!=6||
                    Mathf.Abs((float)table["length_seconds"]-sourceClip.length)>1e-5f)throw new InvalidOperationException("Table shape or clip duration differs.");
                for(int b=0;b<6;b++)if((string)names[b]!=Bones[b].ToString())throw new InvalidOperationException("Bone order differs.");
                var deltas=new Quaternion[481][];
                for(int i=0;i<481;i++)
                {
                    if(!(rows[i] is JArray row)||row.Count!=6)throw new InvalidOperationException("Table row differs.");
                    deltas[i]=new Quaternion[6];
                    for(int b=0;b<6;b++)
                    {
                        var q=rows[i][b] as JArray;if(q==null||q.Count!=4)throw new InvalidOperationException("Quaternion shape differs.");
                        for(int c=0;c<4;c++){float x=(float)q[c];if(float.IsNaN(x)||float.IsInfinity(x))throw new InvalidOperationException("Nonfinite table.");}
                        float xq=(float)q[0],yq=(float)q[1],zq=(float)q[2],wq=(float)q[3];
                        float norm=xq*xq+yq*yq+zq*zq+wq*wq;
                        if(Mathf.Abs(norm-1)>1e-4f)throw new InvalidOperationException("Nonunit table quaternion.");
                        deltas[i][b]=new Quaternion(xq,yq,zq,wq);
                    }
                }
                _deltas=deltas;
            }
            catch(Exception error) when(error is JsonException||error is InvalidOperationException||error is ArgumentException)
            { _failure=error.Message;_deltas=null; }
            failure=_failure;return _deltas!=null;
        }
        internal Quaternion Sample(float phase,int bone)
        {
            float at=Mathf.Repeat(phase,1)*480;int lo=Mathf.Min((int)at,479);float t=at-lo;
            Quaternion q1=_deltas[lo][bone];
            Vector4 Near(Quaternion q){float sign=Quaternion.Dot(q,q1)<0?-1:1;return new Vector4(q.x,q.y,q.z,q.w)*sign;}
            var p0=Near(_deltas[(lo+479)%480][bone]);var p1=Near(q1);var p2=Near(_deltas[(lo+1)%480][bone]);var p3=Near(_deltas[(lo+2)%480][bone]);
            var v=.5f*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t);
            return Quaternion.Normalize(new Quaternion(v.x,v.y,v.z,v.w));
        }
    }
}
