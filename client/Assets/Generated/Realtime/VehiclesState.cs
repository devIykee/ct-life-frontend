// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 5.0.36
// 

namespace CTLife.Realtime {
	public partial class VehiclesState : global::Colyseus.Schema.Schema {
#if UNITY_5_3_OR_NEWER
		[global::UnityEngine.Scripting.Preserve]
#endif
		public VehiclesState() { }

		[global::Colyseus.Schema.Type(0, "map", typeof(global::Colyseus.Schema.MapSchema<VehicleState>))]
		public global::Colyseus.Schema.MapSchema<VehicleState> vehicles = new global::Colyseus.Schema.MapSchema<VehicleState>();
	}
}
