namespace ProjectEve.PhoneOS.Services;

/// <summary>
/// Player identity + run prefs created at New Game.
/// Stored local for now; later maps into SimCharacter + SQLite.
/// </summary>
public class PlayerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsComplete { get; set; }

    // —— Identity ——
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string PreferredName { get; set; } = ""; // what Eve calls you
    public string Gender { get; set; } = "Male";   // Male / Female / Nonbinary / Custom
    public string CustomGender { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public int Age => BirthDate.HasValue
        ? Math.Max(18, (int)((DateTime.Now - BirthDate.Value).TotalDays / 365.25))
        : 25;

    // —— Appearance (prompt DNA) ——
    public string HairColor { get; set; } = "Brown";
    public string HairStyle { get; set; } = "Short";
    public string EyeColor { get; set; } = "Brown";
    public string SkinTone { get; set; } = "Light";
    public string HeightBand { get; set; } = "Average"; // Short / Average / Tall
    public string BodyShape { get; set; } = "Average";
    public string StyleLook { get; set; } = "Casual";   // Casual / Work / Athletic / Alt
    public bool WearsGlasses { get; set; }
    public string GlassesType { get; set; } = "None";   // None / Reading / Always / Fashion
    public string Distinguishing { get; set; } = "";    // scars, tattoos short note

    // —— Work / money ——
    public string JobTitle { get; set; } = "";
    public string Employer { get; set; } = "";
    public string WorkShift { get; set; } = "Days";     // Days / Nights / Mixed / Unemployed
    public decimal StartingCash { get; set; } = 120m;
    public decimal StartingBank { get; set; } = 1800m;
    public decimal StartingDebt { get; set; } = 0m;

    // —— Home / transport ——
    public string HomeType { get; set; } = "Apartment"; // Apartment / House / Farm / Downtown loft
    public string HomeArea { get; set; } = "City limits"; // Downtown / City limits / Country
    public string HomeTown { get; set; } = "Bellefontaine, OH";
    public string Transport { get; set; } = "Car";      // Car / Truck / Bike / Walk / Bus

    // —— Background ——
    public string BackgroundNote { get; set; } = "";
    public string SkillsNote { get; set; } = "";

    // —— Family (light at start; full gen later) ——
    public bool HasPartner { get; set; }
    public bool HasKids { get; set; }
    public string FamilyNote { get; set; } = "";

    // —— Sports / taste seeds ——
    public string FavoriteSport { get; set; } = "Football";
    public string FavoriteTeam { get; set; } = "Ohio State";
    public string MusicMood { get; set; } = "Mixed";

    // —— Eve link ——
    public string EveRelationship { get; set; } = "Just met"; // Just met / Friends / Dating / Complicated
    public string EveHowMet { get; set; } = "Coffee shop";
    public int EveChemistry { get; set; } = 5;  // 1–10 starting pull
    public int EveTrustSeed { get; set; } = 4;  // 1–10

    // —— Run prefs (bias events / content) ——
    public string ContentRating { get; set; } = "Plus18"; // PG / Plus18
    public bool PrefRealLife { get; set; } = true;
    public bool PrefDrama { get; set; }
    public bool PrefRomance { get; set; } = true;
    public bool PrefCrime { get; set; }
    public bool PrefComedy { get; set; } = true;

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(PreferredName) ? PreferredName.Trim()
        : !string.IsNullOrWhiteSpace(FirstName) ? FirstName.Trim()
        : "Player";

    public string FullName =>
        $"{FirstName} {LastName}".Trim();
}