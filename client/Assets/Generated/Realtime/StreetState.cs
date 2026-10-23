// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 5.0.36
// 

namespace CTLife.Realtime {
	public partial class StreetState : global::Colyseus.Schema.Schema {
#if UNITY_5_3_OR_NEWER
		[global::UnityEngine.Scripting.Preserve]
#endif
		public StreetState() { }

		[global::Colyseus.Schema.Type(0, "string")]
		public string district = "";

		[global::Colyseus.Schema.Type(1, "string")]
		public string instanceId = "";

		[global::Colyseus.Schema.Type(2, "map", typeof(global::Colyseus.Schema.MapSchema<PlayerState>))]
		public global::Colyseus.Schema.MapSchema<PlayerState> players = new global::Colyseus.Schema.MapSchema<PlayerState>();
	}
}
