// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 5.0.36
// 

namespace CTLife.Realtime {
	public partial class VideoCallState : global::Colyseus.Schema.Schema {
#if UNITY_5_3_OR_NEWER
		[global::UnityEngine.Scripting.Preserve]
#endif
		public VideoCallState() { }

		[global::Colyseus.Schema.Type(0, "string")]
		public string callId = "";

		[global::Colyseus.Schema.Type(1, "string")]
		public string hostUserId = "";

		[global::Colyseus.Schema.Type(2, "string")]
		public string kind = "video";

		[global::Colyseus.Schema.Type(3, "string")]
		public string status = "live";

		[global::Colyseus.Schema.Type(4, "array", typeof(global::Colyseus.Schema.ArraySchema<string>), "string")]
		public global::Colyseus.Schema.ArraySchema<string> participantUserIds = new global::Colyseus.Schema.ArraySchema<string>();
	}
}
