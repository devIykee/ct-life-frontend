using System;
using CTLife.Core;
using CTLife.Careers;
using CTLife.CityLife;
using CTLife.Economy;
using CTLife.SocialFeatures;
using CTLife.UI;

namespace CTLife.App
{
    /// <summary>The phone catalog. Phase 2 money apps come from CTLife.Features.Money (namespace CTLife.Economy).</summary>
    public static class PhoneCatalog
    {
        public static void Register(PhoneShell phone, Action<string> travel, Action signOut, PhoneApp jobsOverride = null, PhoneApp homeOverride = null)
        {
            phone.Register(new MapApp(travel));
            phone.Register(jobsOverride ?? new CareersApp());
            phone.Register(new MoneyBankApp());
            phone.Register(new RichListApp());
            phone.Register(homeOverride ?? new CityHomeApp());
            phone.Register(new ProfileApp());
            phone.Register(new SettingsApp(signOut));
            phone.Register(new PulseApp());
            phone.Register(new PerpPitApp());
            phone.Register(new PredictionPitApp());
            phone.Register(new OreRefineryApp());
            phone.Register(new MoneyBankApp(topUpTile: true));
            phone.Register(new TimelineApp());
            phone.Register(new MessagesApp());
            phone.Register(new SpacesStudioApp());
            phone.Register(new CallsApp());
            phone.Register(new CityShopApp());
            phone.Register(new CityWeekApp());
            phone.Register(new CityBusinessApp());
            phone.Register(new ComingApp("ads", "Ads", 5, "#778ca3"));
            phone.Register(new ComingApp("league", "League", 6, "#4b7bec"));
        }
    }
}
