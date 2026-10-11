// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 5.0.36
// 

namespace CTLife.Realtime {
	public partial class SpaceState : global::Colyseus.Schema.Schema {
#if UNITY_5_3_OR_NEWER
		[global::UnityEngine.Scripting.Preserve]
#endif
		public SpaceState() { }

		[global::Colyseus.Schema.Type(0, "string")]
		public string spaceId = "";

		[global::Colyseus.Schema.Type(1, "string")]
		public string hostUserId = "";

		[global::Colyseus.Schema.Type(2, "string")]
		public string title = "";

		[global::Colyseus.Schema.Type(3, "string")]
		public string status = "live";

		[global::Colyseus.Schema.Type(4, "map", typeof(global::Colyseus.Schema.MapSchema<SpeakerState>))]
		public global::Colyseus.Schema.MapSchema<SpeakerState> speakers = new global::Colyseus.Schema.MapSchema<SpeakerState>();

		[global::Colyseus.Schema.Type(5, "array", typeof(global::Colyseus.Schema.ArraySchema<string>), "string")]
		public global::Colyseus.Schema.ArraySchema<string> handQueue = new global::Colyseus.Schema.ArraySchema<string>();

		[global::Colyseus.Schema.Type(6, "number")]
		public double listenerCount = 0;
	}
}
