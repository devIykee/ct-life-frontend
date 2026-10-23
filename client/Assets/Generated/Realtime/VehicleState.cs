// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 5.0.36
// 

namespace CTLife.Realtime {
	public partial class VehicleState : global::Colyseus.Schema.Schema {
#if UNITY_5_3_OR_NEWER
		[global::UnityEngine.Scripting.Preserve]
#endif
		public VehicleState() { }

		[global::Colyseus.Schema.Type(0, "string")]
		public string id = "";

		[global::Colyseus.Schema.Type(1, "string")]
		public string kind = "";

		[global::Colyseus.Schema.Type(2, "string")]
		public string driver = "";

		[global::Colyseus.Schema.Type(3, "float32")]
		public double x = 0;

		[global::Colyseus.Schema.Type(4, "float32")]
		public double y = 0;

		[global::Colyseus.Schema.Type(5, "float32")]
		public double z = 0;

		[global::Colyseus.Schema.Type(6, "float32")]
		public double yaw = 0;

		[global::Colyseus.Schema.Type(7, "float32")]
		public double pitch = 0;

		[global::Colyseus.Schema.Type(8, "float32")]
		public double roll = 0;

		[global::Colyseus.Schema.Type(9, "float32")]
		public double speed = 0;

		[global::Colyseus.Schema.Type(10, "uint16")]
		public ushort flags = 0;
	}
}
