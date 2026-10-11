// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 5.0.36
// 

namespace CTLife.Realtime {
	public partial class CityState : global::Colyseus.Schema.Schema {
#if UNITY_5_3_OR_NEWER
		[global::UnityEngine.Scripting.Preserve]
#endif
		public CityState() { }

		[global::Colyseus.Schema.Type(0, "int32")]
		public int online = 0;

		[global::Colyseus.Schema.Type(1, "int32")]
		public int mood = 0;

		[global::Colyseus.Schema.Type(2, "array", typeof(global::Colyseus.Schema.ArraySchema<string>), "string")]
		public global::Colyseus.Schema.ArraySchema<string> eventIds = new global::Colyseus.Schema.ArraySchema<string>();

		[global::Colyseus.Schema.Type(3, "string")]
		public string metaId = "";
	}
}
