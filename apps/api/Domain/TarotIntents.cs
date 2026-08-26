namespace TarotDestiny.Api.Domain;

public static class TarotIntents
{
    public const string PersonalCustom = "PERSONAL_CUSTOM";

    public static readonly string[] All =
    [
        "GENERAL_DAILY",
        "GENERAL_DECISION",
        "GENERAL_DIRECTION",
        "LOVE_GENERAL",
        "LOVE_SINGLE",
        "LOVE_RELATIONSHIP",
        "LOVE_BREAKUP",
        "LOVE_RECONCILIATION",
        "LOVE_NEW_PERSON",
        "LOVE_COMMITMENT",
        "LOVE_DECISION",
        "CAREER_GENERAL",
        "CAREER_NEW_JOB",
        "CAREER_CHANGE_JOB",
        "CAREER_PROMOTION",
        "CAREER_BUSINESS",
        "CAREER_DECISION",
        "CAREER_CONFLICT",
        "MONEY_GENERAL",
        "MONEY_INCOME",
        "MONEY_INVESTMENT",
        "MONEY_BUSINESS",
        "MONEY_DEBT",
        "MONEY_PURCHASE",
        "MONEY_DECISION",
        "FAMILY_GENERAL",
        "FAMILY_CONFLICT",
        "PERSONAL_GROWTH_GENERAL",
        "PERSONAL_GROWTH_HEALING",
        PersonalCustom
    ];
}
