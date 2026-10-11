using System;
using CTLife.Core;
namespace CTLife.World
{
    /// <summary>Presentation for an exact server-confirmed occupied spot. Never changes wire state or occupancy.</summary>
    public static class OccupiedSpotAnimation
    {
        public static string Resolve(VenueSceneRoot venue,string serverSpotId,string serverAnim)
        {
            if(venue==null||string.IsNullOrEmpty(serverSpotId)||string.IsNullOrEmpty(serverAnim))return serverAnim;
            var spot=venue.SpotDef(serverSpotId);
            // Local aliases are useful for navigation, but only the exact registry wire id is authority here.
            if(spot==null||!string.Equals(spot.wireId,serverSpotId,StringComparison.Ordinal)||string.IsNullOrEmpty(spot.anim))return serverAnim;
            string generic=spot.kind=="stage"?"act_type":"act_sit";
            if(!string.Equals(serverAnim,generic,StringComparison.Ordinal))return serverAnim;
            if(!Services.TryGet<ConfigStore>(out var config)||config.Registries==null||
                !config.Registries.Anims.TryGetValue(spot.anim,out var animation)||!animation.loop)return serverAnim;
            // An occupied host may stand idle; moving locomotion, reactions and emotes never become seat bases.
            if(animation.kind!=AnimKind.Act&&!(animation.kind==AnimKind.Loco&&animation.id=="loco_idle"))return serverAnim;
            return animation.id;
        }
    }
}
