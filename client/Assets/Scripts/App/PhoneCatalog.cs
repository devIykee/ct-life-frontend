using System;
using CTLife.Core;
using CTLife.Careers;
using CTLife.CityLife;
using CTLife.Competition;
using CTLife.Economy;
using CTLife.SocialFeatures;
using CTLife.UI;
using CTLife.Ventures;

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
            // Phase 5 (Ventures lane): "ads" keeps the placeholder's id so the lobby tile still opens it. One shared VenturesHub.
            phone.Register(new AdDeskApp());
            phone.Register(new BuilderApp());
            phone.Register(new VcRowApp());
            // Phase 6 (Competition lane): one shared CompetitionHub; "league" keeps the placeholder's id.
            phone.Register(new LeagueApp());
            phone.Register(new BeefApp());
            phone.Register(new FarmApp());
            phone.Register(new CabalApp());
            phone.Register(new NpcChatApp());
            phone.Register(new XLinkApp());
        }
    }
}
