// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 5.0.36
// 

namespace CTLife.Realtime {
	public partial class NpcState : global::Colyseus.Schema.Schema {
#if UNITY_5_3_OR_NEWER
		[global::UnityEngine.Scripting.Preserve]
#endif
		public NpcState() { }

		[global::Colyseus.Schema.Type(0, "string")]
		public string archetypeId = "";

		[global::Colyseus.Schema.Type(1, "string")]
		public string state = "idle";

		[global::Colyseus.Schema.Type(2, "string")]
		public string spotId = "";

		[global::Colyseus.Schema.Type(3, "string")]
		public string animId = "";

		[global::Colyseus.Schema.Type(4, "string")]
		public string targetUserId = "";

		[global::Colyseus.Schema.Type(5, "string")]
		public string lineId = "";

		[global::Colyseus.Schema.Type(6, "map", typeof(global::Colyseus.Schema.MapSchema<string>), "string")]
		public global::Colyseus.Schema.MapSchema<string> lineParams = new global::Colyseus.Schema.MapSchema<string>();

		[global::Colyseus.Schema.Type(7, "string")]
		public string emoteId = "";

		[global::Colyseus.Schema.Type(8, "number")]
		public double startedAt = 0;
	}
}
