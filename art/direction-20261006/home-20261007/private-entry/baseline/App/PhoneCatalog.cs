using System;
using CTLife.Core;
using CTLife.UI;

namespace CTLife.App
{
    /// <summary>One phone catalog shared by the live flow and development presentation review.</summary>
    public static class PhoneCatalog
    {
        public static void Register(PhoneShell phone, Action<string> travel, Action signOut, PhoneApp jobsOverride = null)
        {
            phone.Register(new MapApp(travel));
            phone.Register(jobsOverride ?? new JobsApp());
            phone.Register(new BankApp());
            phone.Register(new RichListApp());
            phone.Register(new HomeApp());
            phone.Register(new ProfileApp());
            phone.Register(new SettingsApp(signOut));
            phone.Register(new ComingApp("pulse", "Pulse", 2, "#20bf6b", Features.Markets));
            phone.Register(new PerpsApp());
            phone.Register(new ComingApp("predict", "Predict", 2, "#3867d6", Features.Markets));
            phone.Register(new ComingApp("ore", "ORE", 2, "#fd9644", Features.OreReader));
            phone.Register(new ComingApp("topup", "Top up", 2, "#f7b731", Features.Payments));
            phone.Register(new ComingApp("timeline", "Timeline", 3, "#2d98da", Features.Feed));
            phone.Register(new ComingApp("messages", "Messages", 3, "#26de81", Features.Dms));
            phone.Register(new SpacesApp());
            phone.Register(new ComingApp("calls", "Calls", 3, "#0fb9b1", Features.Voice));
            phone.Register(new ComingApp("shop", "Shop", 4, "#fc5c65"));
            phone.Register(new ComingApp("ads", "Ads", 5, "#778ca3"));
            phone.Register(new ComingApp("league", "League", 6, "#4b7bec"));
        }
    }
}
