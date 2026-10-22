// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 5.0.36
// 

namespace CTLife.Realtime {
	public partial class PlayerState : global::Colyseus.Schema.Schema {
#if UNITY_5_3_OR_NEWER
		[global::UnityEngine.Scripting.Preserve]
#endif
		public PlayerState() { }

		[global::Colyseus.Schema.Type(0, "string")]
		public string userId = "";

		[global::Colyseus.Schema.Type(1, "string")]
		public string name = "";

		[global::Colyseus.Schema.Type(2, "string")]
		public string skrName = "";

		[global::Colyseus.Schema.Type(3, "string")]
		public string tribeId = "";

		[global::Colyseus.Schema.Type(4, "boolean")]
		public bool isSeeker = false;

		[global::Colyseus.Schema.Type(5, "string")]
		public string minerTier = "none";

		[global::Colyseus.Schema.Type(6, "array", typeof(global::Colyseus.Schema.ArraySchema<string>), "string")]
		public global::Colyseus.Schema.ArraySchema<string> look = new global::Colyseus.Schema.ArraySchema<string>();

		[global::Colyseus.Schema.Type(7, "float32")]
		public double x = 0;

		[global::Colyseus.Schema.Type(8, "float32")]
		public double z = 0;

		[global::Colyseus.Schema.Type(9, "float32")]
		public double rotY = 0;

		[global::Colyseus.Schema.Type(10, "string")]
		public string anim = "loco_idle";

		[global::Colyseus.Schema.Type(11, "string")]
		public string emoteId = "";

		[global::Colyseus.Schema.Type(12, "number")]
		public double emoteAt = 0;

		[global::Colyseus.Schema.Type(13, "string")]
		public string spotId = "";

		[global::Colyseus.Schema.Type(14, "boolean")]
		public bool talking = false;

		[global::Colyseus.Schema.Type(15, "float32")]
		public double ix = 0;

		[global::Colyseus.Schema.Type(16, "float32")]
		public double iz = 0;

		[global::Colyseus.Schema.Type(17, "float32")]
		public double irotY = 0;
	}
}
