// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 5.0.36
// 

namespace CTLife.Realtime {
	public partial class SpeakerState : global::Colyseus.Schema.Schema {
#if UNITY_5_3_OR_NEWER
		[global::UnityEngine.Scripting.Preserve]
#endif
		public SpeakerState() { }

		[global::Colyseus.Schema.Type(0, "string")]
		public string userId = "";

		[global::Colyseus.Schema.Type(1, "string")]
		public string role = "listener";

		[global::Colyseus.Schema.Type(2, "boolean")]
		public bool canPublish = false;

		[global::Colyseus.Schema.Type(3, "boolean")]
		public bool handRaised = false;

		[global::Colyseus.Schema.Type(4, "number")]
		public double handRaisedAt = 0;
	}
}
