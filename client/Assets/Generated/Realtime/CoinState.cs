// 
// THIS FILE HAS BEEN GENERATED AUTOMATICALLY
// DO NOT CHANGE IT MANUALLY UNLESS YOU KNOW WHAT YOU'RE DOING
// 
// GENERATED USING @colyseus/schema 5.0.36
// 

namespace CTLife.Realtime {
	public partial class CoinState : global::Colyseus.Schema.Schema {
#if UNITY_5_3_OR_NEWER
		[global::UnityEngine.Scripting.Preserve]
#endif
		public CoinState() { }

		[global::Colyseus.Schema.Type(0, "string")]
		public string coinId = "";

		[global::Colyseus.Schema.Type(1, "string")]
		public string ticker = "";

		[global::Colyseus.Schema.Type(2, "string")]
		public string name = "";

		[global::Colyseus.Schema.Type(3, "string")]
		public string stage = "new";

		[global::Colyseus.Schema.Type(4, "int64")]
		public long priceMicros = default(long);

		[global::Colyseus.Schema.Type(5, "int64")]
		public long mcapCents = default(long);

		[global::Colyseus.Schema.Type(6, "int64")]
		public long vol5mCents = default(long);

		[global::Colyseus.Schema.Type(7, "int32")]
		public int change5mBps = 0;

		[global::Colyseus.Schema.Type(8, "int32")]
		public int holders = 0;

		[global::Colyseus.Schema.Type(9, "int64")]
		public long launchedAt = default(long);
	}
}
