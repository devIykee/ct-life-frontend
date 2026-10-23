using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// An affordable, on-demand physics ragdoll for a Humanoid character (11 bodies, character joints), built only when a
    /// knockdown happens and torn down after the get-up, so idle crowds pay nothing. Cast models are imported with an
    /// optimized hierarchy; the rig de-optimizes the one character (AnimatorUtility, runtime API) for the fall and
    /// re-optimizes it afterwards. Budget: at most <see cref="MaxActive"/> at once (Seeker, Mali-G615).
    /// </summary>
    public sealed class RagdollRig
    {
        public const int MaxActive = 2;
        public static int Active { get; private set; }
        private static bool _layersSet;

        private struct BodyPart
        {
            public HumanBodyBones bone;
            public Transform t;
            public Rigidbody rb;
            public Collider col;
            public CharacterJoint joint;
        }

        private readonly Animator _animator;
        private readonly List<BodyPart> _parts = new List<BodyPart>(11);
        private readonly bool _deoptimized;
        private readonly string[] _exposedBones;
        private bool _released;
        private float _stillFor;

        public Transform Hips { get; private set; }
        public Transform Head { get; private set; }
        public Transform Chest { get; private set; }
        public Rigidbody HipsBody { get; private set; }
        public bool IsBuilt => _parts.Count > 0;

        /// <summary>Claims a slot in the budget; false when two ragdolls are already simulating.</summary>
        public static bool TryAcquire()
        {
            if (Active >= MaxActive) return false;
            Active++;
            return true;
        }

        private RagdollRig(Animator animator, bool deoptimized, string[] exposedBones)
        { _animator = animator; _deoptimized = deoptimized; _exposedBones = exposedBones; }

        /// <summary>Returns a slot claimed by <see cref="TryAcquire"/> when no rig could be built.</summary>
        public static void ReleaseUnbuilt() => Active = Mathf.Max(0, Active - 1);

        /// <summary>Builds the bodies on the character (call after <see cref="TryAcquire"/>). Null when it has no Humanoid.</summary>
        public static RagdollRig Build(Animator animator)
        {
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return null;
            if (!_layersSet)
            {
                _layersSet = true;
                // A falling body must not shove the capsules of the characters around it (or its own).
                Physics.IgnoreLayerCollision(CharacterLayers.Characters, CharacterLayers.Ragdoll, true);
            }
            bool deopt = !animator.hasTransformHierarchy;
            var exposed = new List<string>();
            if (deopt)
            {
                // Keep imported attachment/measurement bones (currently the cast Hips) when restoring optimization.
                // Otherwise a person who has fallen once loses the pelvis Transform needed for their next car seat.
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var bone = animator.GetBoneTransform((HumanBodyBones)i);
                    if (bone != null) exposed.Add(bone.name);
                }
                // De-optimizing re-initializes the Animator (bones come back in the bind pose). Re-pose the exact state,
                // phase and parameters first so the body falls from the pose the player saw, not from a T-pose.
                var info = animator.GetCurrentAnimatorStateInfo(0);
                var floats = new List<KeyValuePair<int, float>>();
                foreach (var p in animator.parameters)
                    if (p.type == AnimatorControllerParameterType.Float) floats.Add(new KeyValuePair<int, float>(p.nameHash, animator.GetFloat(p.nameHash)));
                AnimatorUtility.DeoptimizeTransformHierarchy(animator.gameObject);
                foreach (var f in floats) animator.SetFloat(f.Key, f.Value);
                animator.Play(info.fullPathHash, 0, info.normalizedTime);
                animator.Update(0f);
            }
            var rig = new RagdollRig(animator, deopt, exposed.ToArray());
            if (!rig.Create()) { rig._released = true; rig.Teardown(); return null; } // the caller releases the slot
            return rig;
        }

        private bool Create()
        {
            Transform B(HumanBodyBones b) => _animator.GetBoneTransform(b);
            Hips = B(HumanBodyBones.Hips);
            Chest = B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
            Head = B(HumanBodyBones.Head);
            var lUpLeg = B(HumanBodyBones.LeftUpperLeg); var rUpLeg = B(HumanBodyBones.RightUpperLeg);
            var lLeg = B(HumanBodyBones.LeftLowerLeg); var rLeg = B(HumanBodyBones.RightLowerLeg);
            var lFoot = B(HumanBodyBones.LeftFoot); var rFoot = B(HumanBodyBones.RightFoot);
            var lArm = B(HumanBodyBones.LeftUpperArm); var rArm = B(HumanBodyBones.RightUpperArm);
            var lFore = B(HumanBodyBones.LeftLowerArm); var rFore = B(HumanBodyBones.RightLowerArm);
            var lHand = B(HumanBodyBones.LeftHand); var rHand = B(HumanBodyBones.RightHand);
            var neck = B(HumanBodyBones.Neck) ?? Head;
            if (Hips == null || Chest == null || Head == null || lUpLeg == null || rUpLeg == null || lLeg == null || rLeg == null ||
                lArm == null || rArm == null || lFore == null || rFore == null || lFoot == null || rFoot == null || lHand == null || rHand == null)
                return false;

            // Character axes from the current pose (works standing or mid-stride).
            var up = (neck.position - Hips.position).normalized;
            var right = (rUpLeg.position - lUpLeg.position).normalized;
            var forward = Vector3.Cross(right, up).normalized;

            float legScale = Vector3.Distance(lUpLeg.position, lLeg.position);
            Add(HumanBodyBones.Hips, Hips, null, 12.5f, Box(Hips, Hips.position, Chest.position, Vector3.Distance(lUpLeg.position, rUpLeg.position) * 1.5f), default, default, 0, 0, 0);
            Add(HumanBodyBones.Chest, Chest, HipsBody, 12.5f, Capsule(Chest, Chest.position, neck.position, legScale * 0.32f), right, forward, -20, 20, 10);
            Add(HumanBodyBones.Head, Head, Part(HumanBodyBones.Chest), 5f, Sphere(Head, legScale * 0.27f), right, forward, -40, 25, 25);
            Add(HumanBodyBones.LeftUpperLeg, lUpLeg, HipsBody, 7.5f, Capsule(lUpLeg, lUpLeg.position, lLeg.position, legScale * 0.18f), right, forward, -20, 70, 30);
            Add(HumanBodyBones.RightUpperLeg, rUpLeg, HipsBody, 7.5f, Capsule(rUpLeg, rUpLeg.position, rLeg.position, legScale * 0.18f), right, forward, -20, 70, 30);
            Add(HumanBodyBones.LeftLowerLeg, lLeg, Part(HumanBodyBones.LeftUpperLeg), 5f, Capsule(lLeg, lLeg.position, lFoot.position, legScale * 0.14f), right, forward, -90, 0, 0);
            Add(HumanBodyBones.RightLowerLeg, rLeg, Part(HumanBodyBones.RightUpperLeg), 5f, Capsule(rLeg, rLeg.position, rFoot.position, legScale * 0.14f), right, forward, -90, 0, 0);
            Add(HumanBodyBones.LeftUpperArm, lArm, Part(HumanBodyBones.Chest), 2.5f, Capsule(lArm, lArm.position, lFore.position, legScale * 0.12f), forward, up, -70, 10, 50);
            Add(HumanBodyBones.RightUpperArm, rArm, Part(HumanBodyBones.Chest), 2.5f, Capsule(rArm, rArm.position, rFore.position, legScale * 0.12f), forward, up, -70, 10, 50);
            Add(HumanBodyBones.LeftLowerArm, lFore, Part(HumanBodyBones.LeftUpperArm), 2f, Capsule(lFore, lFore.position, lHand.position, legScale * 0.1f), up, forward, -90, 0, 0);
            Add(HumanBodyBones.RightLowerArm, rFore, Part(HumanBodyBones.RightUpperArm), 2f, Capsule(rFore, rFore.position, rHand.position, legScale * 0.1f), up, forward, -90, 0, 0);
            return true;
        }

        private Rigidbody Part(HumanBodyBones b)
        {
            foreach (var p in _parts) if (p.bone == b) return p.rb;
            return HipsBody;
        }

        private readonly Dictionary<GameObject, int> _layers = new Dictionary<GameObject, int>();

        private void Add(HumanBodyBones bone, Transform t, Rigidbody parent, float mass, Collider col, Vector3 twistAxisWorld, Vector3 swingAxisWorld,
            float lowTwist, float highTwist, float swing)
        {
            if (!_layers.ContainsKey(t.gameObject)) _layers[t.gameObject] = t.gameObject.layer;
            t.gameObject.layer = CharacterLayers.Ragdoll;
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = bone == HumanBodyBones.Hips ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.6f;
            CharacterJoint joint = null;
            if (parent != null)
            {
                joint = t.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parent;
                joint.axis = t.InverseTransformDirection(twistAxisWorld).normalized;
                joint.swingAxis = t.InverseTransformDirection(swingAxisWorld).normalized;
                joint.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
                joint.highTwistLimit = new SoftJointLimit { limit = highTwist };
                joint.swing1Limit = new SoftJointLimit { limit = swing };
                joint.swing2Limit = new SoftJointLimit { limit = swing };
                joint.enableProjection = true;
                joint.enablePreprocessing = false;
            }
            if (bone == HumanBodyBones.Hips) HipsBody = rb;
            _parts.Add(new BodyPart { bone = bone, t = t, rb = rb, col = col, joint = joint });
        }

        private static CapsuleCollider Capsule(Transform t, Vector3 from, Vector3 to, float radius)
        {
            var c = t.gameObject.AddComponent<CapsuleCollider>();
            var a = t.InverseTransformPoint(from); var b = t.InverseTransformPoint(to);
            var d = b - a;
            int axis = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? (Mathf.Abs(d.x) > Mathf.Abs(d.z) ? 0 : 2) : (Mathf.Abs(d.y) > Mathf.Abs(d.z) ? 1 : 2);
            c.direction = axis;
            c.center = (a + b) * 0.5f;
            float scale = Mathf.Max(1e-4f, t.lossyScale.x);
            c.radius = radius / scale;
            c.height = d.magnitude + c.radius * 2f;
            return c;
        }

        private static BoxCollider Box(Transform t, Vector3 from, Vector3 to, float width)
        {
            var c = t.gameObject.AddComponent<BoxCollider>();
            var a = t.InverseTransformPoint(from); var b = t.InverseTransformPoint(to);
            float scale = Mathf.Max(1e-4f, t.lossyScale.x);
            c.center = (a + b) * 0.5f;
            float len = (b - a).magnitude;
            c.size = new Vector3(width / scale, width / scale, width / scale) * 0.6f + new Vector3(0, len, 0);
            return c;
        }

        private static SphereCollider Sphere(Transform t, float radius)
        {
            var c = t.gameObject.AddComponent<SphereCollider>();
            float scale = Mathf.Max(1e-4f, t.lossyScale.x);
            c.radius = radius / scale;
            c.center = t.InverseTransformDirection(Vector3.up) * c.radius * 0.6f;
            return c;
        }

        /// <summary>Hand the body to physics: the animator stops; every bone keeps its current pose and velocity.</summary>
        public void Activate(Vector3 velocity, Vector3 impulse, Vector3 impulsePoint)
        {
            _animator.enabled = false;
            foreach (var p in _parts)
            {
                p.rb.isKinematic = false;
                p.rb.linearVelocity = velocity;
                p.rb.WakeUp();
            }
            if (Chest != null && impulse.sqrMagnitude > 0f)
            {
                var chest = Part(HumanBodyBones.Chest);
                if (chest != null) chest.AddForceAtPosition(impulse, impulsePoint, ForceMode.Impulse);
            }
            _stillFor = 0f;
        }

        /// <summary>True once the body has come to rest (or after <paramref name="maxSeconds"/>).</summary>
        public bool Settled(float dt, float elapsed, float maxSeconds = 3.5f)
        {
            if (HipsBody == null) return true;
            if (HipsBody.linearVelocity.magnitude < 0.12f && HipsBody.angularVelocity.magnitude < 0.6f) _stillFor += dt; else _stillFor = 0f;
            return _stillFor > 0.45f || elapsed > maxSeconds;
        }

        /// <summary>Freezes the bodies in place (kinematic) so the get-up can blend from the rest pose.</summary>
        public void Freeze()
        {
            foreach (var p in _parts) if (p.rb != null) { p.rb.isKinematic = true; }
        }

        /// <summary>Character facing while lying: the chest's forward (up = face-up), from the leg/spine axes.</summary>
        public bool FaceUp(out Vector3 headDirection)
        {
            var lUpLeg = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var rUpLeg = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var spine = (Head.position - Hips.position);
            headDirection = new Vector3(spine.x, 0f, spine.z).normalized;
            var right = (rUpLeg.position - lUpLeg.position).normalized;
            var forward = Vector3.Cross(right, spine.normalized);
            return forward.y >= 0f;
        }

        /// <summary>Removes every physics component and re-optimizes the hierarchy (which re-initializes the Animator).</summary>
        public void Teardown()
        {
            foreach (var p in _parts)
            {
                if (p.joint != null) Object.DestroyImmediate(p.joint);
                if (p.rb != null) Object.DestroyImmediate(p.rb);
                if (p.col != null) Object.DestroyImmediate(p.col);
            }
            _parts.Clear();
            foreach (var kv in _layers) if (kv.Key != null) kv.Key.layer = kv.Value;
            _layers.Clear();
            if (_animator != null)
            {
                _animator.enabled = true;
                if (_deoptimized) AnimatorUtility.OptimizeTransformHierarchy(_animator.gameObject, _exposedBones);
            }
            if (!_released) { _released = true; Active = Mathf.Max(0, Active - 1); }
        }
    }
}
