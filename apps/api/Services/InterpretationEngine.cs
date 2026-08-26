using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
namespace TarotDestiny.Api.Services;

public sealed record InterpretationPayload(
    TarotDomain Domain,
    string Intent,
    string Locale,
    string Spread,
    string MainTheme,
    string Opportunity,
    string Challenge,
    IReadOnlyList<CardInterpretationSeed> Cards)
{
    public string OverallNarrative { get; init; } = string.Empty;
    public string Guidance { get; init; } = string.Empty;
    public string ReflectionQuestion { get; init; } = string.Empty;
    public string DominantElement { get; init; } = string.Empty;
    public int ReversedCount { get; init; }
    public IReadOnlyList<string> RelationshipSignals { get; init; } = [];
}

public sealed record CardInterpretationSeed(
    string Position,
    string CardId,
    string CardName,
    Orientation Orientation,
    IReadOnlyList<string> Keywords)
{
    public string CoreMeaning { get; init; } = string.Empty;
    public string PositionMeaning { get; init; } = string.Empty;
    public string DomainMeaning { get; init; } = string.Empty;
    public string NarrativeRole { get; init; } = string.Empty;
    public IReadOnlyList<string> MeaningKeywords { get; init; } = [];
}

public interface IInterpretationEngine
{
    InterpretationPayload Build(TarotReadingDto request, ClassificationResult classification);
}

public sealed class RuleInterpretationEngine(ITarotCatalog catalog) : IInterpretationEngine
{
    public InterpretationPayload Build(TarotReadingDto request, ClassificationResult classification)
    {
        var thai = IsThai(request.Locale);
        var domainMeaning = DomainMeaning(classification.Domain, thai);
        var resolved = request.Cards.Select(card => ResolveCard(card, request.Locale, domainMeaning, thai)).ToArray();

        var dominantElement = ResolveDominantElement(resolved);
        var reversedCount = resolved.Count(card => card.Request.Orientation == Orientation.REVERSED);
        var relationshipSignals = BuildRelationshipSignals(resolved, dominantElement, reversedCount, thai);
        var overallNarrative = BuildOverallNarrative(resolved, dominantElement, thai);
        var opportunity = BuildOpportunity(resolved, classification.Domain, thai);
        var challenge = BuildChallenge(resolved, dominantElement, thai);
        var guidance = BuildGuidance(resolved, classification.Domain, thai);

        var cards = resolved.Select(card => new CardInterpretationSeed(
            card.Request.Position,
            card.Meaning.Id,
            card.Localized.Name,
            card.Request.Orientation,
            [card.OrientationMeaning.Summary, card.PositionMeaning, card.DomainMeaning])
        {
            CoreMeaning = card.OrientationMeaning.Summary,
            PositionMeaning = card.PositionMeaning,
            DomainMeaning = card.DomainMeaning,
            NarrativeRole = NarrativeRole(card.Request.Position, card.Request.Orientation, thai),
            MeaningKeywords = card.OrientationMeaning.Keywords
        }).ToArray();

        return new InterpretationPayload(
            classification.Domain,
            classification.Intent,
            thai ? "th" : "en",
            request.Spread,
            BuildMainTheme(resolved, classification.Domain, thai),
            opportunity,
            challenge,
            cards)
        {
            OverallNarrative = overallNarrative,
            Guidance = guidance,
            ReflectionQuestion = ReflectionQuestion(classification.Domain, thai),
            DominantElement = LocalizedElement(dominantElement, thai),
            ReversedCount = reversedCount,
            RelationshipSignals = relationshipSignals
        };
    }

    private ResolvedCard ResolveCard(SelectedCard card, string locale, string domainMeaning, bool thai)
    {
        var meaning = catalog.Get(card.CardId);
        var localized = meaning.ForLocale(locale);
        var orientationMeaning = meaning.ForOrientation(locale, card.Orientation);

        return new ResolvedCard(
            card,
            meaning,
            localized,
            orientationMeaning,
            PositionMeaning(card.Position, thai),
            domainMeaning);
    }

    private static string BuildMainTheme(
        IReadOnlyList<ResolvedCard> cards,
        TarotDomain domain,
        bool thai)
    {
        if (cards.Count == 1)
        {
            return thai
                ? $"{DomainLabel(domain, true)}: {cards[0].OrientationMeaning.Summary}"
                : $"{DomainLabel(domain, false)}: {cards[0].OrientationMeaning.Summary}";
        }

        return thai
            ? $"{DomainLabel(domain, true)}กำลังเคลื่อนจาก {FirstKeyword(cards[0])} ผ่าน {FirstKeyword(cards[1])} ไปสู่ {FirstKeyword(cards[^1])}"
            : $"{DomainLabel(domain, false)} moves from {FirstKeyword(cards[0])}, through {FirstKeyword(cards[1])}, toward {FirstKeyword(cards[^1])}.";
    }

    private static string BuildOverallNarrative(
        IReadOnlyList<ResolvedCard> cards,
        string dominantElement,
        bool thai)
    {
        if (cards.Count == 1)
        {
            return thai
                ? $"ไพ่ใบนี้ให้บทเรียนหลักว่า {cards[0].OrientationMeaning.Summary} น้ำหนักของธาตุ{LocalizedElement(dominantElement, true)}บอกให้พิจารณาเรื่องนี้ผ่าน{ElementLens(dominantElement, true)}"
                : $"This card's central lesson is that {cards[0].OrientationMeaning.Summary} Its {LocalizedElement(dominantElement, false)} tone asks you to work through {ElementLens(dominantElement, false)}.";
        }

        var first = cards[0];
        var present = cards[1];
        var direction = cards[^1];
        return thai
            ? $"รากเดิมคือ {first.OrientationMeaning.Summary} ปัจจุบันเรื่องกำลังแสดงผ่าน {present.OrientationMeaning.Summary} และทิศทางที่ควรพัฒนาไปคือ {direction.OrientationMeaning.Summary} ภาพรวมเน้น{ElementLens(dominantElement, true)}"
            : $"The underlying pattern is that {first.OrientationMeaning.Summary} It is now expressed through this truth: {present.OrientationMeaning.Summary} The constructive direction is that {direction.OrientationMeaning.Summary} Overall, the spread emphasizes {ElementLens(dominantElement, false)}.";
    }

    private static IReadOnlyList<string> BuildRelationshipSignals(
        IReadOnlyList<ResolvedCard> cards,
        string dominantElement,
        int reversedCount,
        bool thai)
    {
        var signals = new List<string>();
        var majorCount = cards.Count(card => string.Equals(card.Meaning.Arcana, "MAJOR", StringComparison.Ordinal));

        signals.Add(thai
            ? $"ธาตุเด่นคือ{LocalizedElement(dominantElement, true)} จึงให้น้ำหนักกับ{ElementLens(dominantElement, true)}"
            : $"The dominant element is {LocalizedElement(dominantElement, false)}, emphasizing {ElementLens(dominantElement, false)}.");

        if (majorCount > 0)
        {
            signals.Add(thai
                ? $"ไพ่ชุดใหญ่ {majorCount} ใบชี้ว่าเรื่องนี้เกี่ยวกับบทเรียนหรือจุดเปลี่ยนที่มีน้ำหนักมากกว่าสถานการณ์ชั่วคราว"
                : $"{majorCount} Major Arcana card(s) make this a broader lesson or turning point, not only a temporary event.");
        }

        signals.Add(reversedCount switch
        {
            0 when thai => "ไพ่ตั้งตรงทั้งหมดบอกว่าพลังของเรื่องพร้อมแสดงออกผ่านการกระทำภายนอก",
            0 => "All cards are upright, so the pattern is available for visible action and direct expression.",
            var count when count == cards.Count && thai => "ไพ่กลับหัวทั้งหมดบอกว่างานหลักอยู่ที่การคลี่คลายภายในและอุปสรรคที่ยังไม่ถูกยอมรับ",
            var count when count == cards.Count => "All cards are reversed, so the primary work is internal: acknowledge blocks before forcing outward action.",
            _ when thai => "ไพ่ตั้งตรงและกลับหัวผสมกันบอกว่ามีทั้งแรงส่งและแรงต้าน ควรแก้จุดติดขัดก่อนใช้โอกาสที่เปิดอยู่",
            _ => "Mixed orientations show both momentum and resistance; address the block before fully using the available opening."
        });

        if (cards.Count > 1)
        {
            signals.Add(thai
                ? $"ลำดับเรื่องเปลี่ยนจาก {cards[0].Localized.Name} ไปสู่ {cards[^1].Localized.Name} จึงเป็นการเปลี่ยนจาก {FirstKeyword(cards[0])} ไปหา {FirstKeyword(cards[^1])}"
                : $"The sequence from {cards[0].Localized.Name} to {cards[^1].Localized.Name} shifts the story from {FirstKeyword(cards[0])} toward {FirstKeyword(cards[^1])}.");
        }

        return signals;
    }

    private static string BuildOpportunity(
        IReadOnlyList<ResolvedCard> cards,
        TarotDomain domain,
        bool thai)
    {
        var direction = cards[^1];
        var orientation = direction.Request.Orientation;
        if (thai)
        {
            return orientation == Orientation.UPRIGHT
                ? $"โอกาสอยู่ที่การนำสารของ {direction.Localized.Name} มาใช้กับ{DomainAction(domain, true)}: {direction.OrientationMeaning.Summary}"
                : $"โอกาสเกิดจากการมองเห็นจุดติดขัดของ {direction.Localized.Name} อย่างตรงไปตรงมา แล้วค่อยลงมือกับ{DomainAction(domain, true)}";
        }

        return orientation == Orientation.UPRIGHT
            ? $"The opportunity is to apply {direction.Localized.Name} to {DomainAction(domain, false)}: {direction.OrientationMeaning.Summary}"
            : $"The opportunity comes from naming the block shown by {direction.Localized.Name}, then returning to {DomainAction(domain, false)}.";
    }

    private static string BuildChallenge(
        IReadOnlyList<ResolvedCard> cards,
        string dominantElement,
        bool thai)
    {
        var reversed = cards.Where(card => card.Request.Orientation == Orientation.REVERSED).ToArray();
        if (reversed.Length > 0)
        {
            var names = string.Join(thai ? " และ " : " and ", reversed.Select(card => card.Localized.Name));
            return thai
                ? $"ความท้าทายคือไม่ปล่อยให้รูปแบบของ {names} ทำงานโดยไม่รู้ตัว: {reversed[0].OrientationMeaning.Summary}"
                : $"The challenge is not to let the pattern shown by {names} operate unconsciously: {reversed[0].OrientationMeaning.Summary}";
        }

        return thai
            ? $"ความท้าทายคือการใช้พลังธาตุ{LocalizedElement(dominantElement, true)}อย่างพอดี ไม่ให้กลายเป็น{ElementExcess(dominantElement, true)}"
            : $"The challenge is to use {LocalizedElement(dominantElement, false)} energy in proportion, without letting it become {ElementExcess(dominantElement, false)}.";
    }

    private static string BuildGuidance(
        IReadOnlyList<ResolvedCard> cards,
        TarotDomain domain,
        bool thai)
    {
        var direction = cards[^1];
        return thai
            ? $"เริ่มจากการกระทำเล็กที่ตรวจสอบได้หนึ่งอย่างเกี่ยวกับ{DomainAction(domain, true)} ใช้ {direction.Localized.Name} เป็นหลักว่า {direction.OrientationMeaning.Summary} แล้วทบทวนผลก่อนก้าวถัดไป"
            : $"Take one small, observable action around {DomainAction(domain, false)}. Use {direction.Localized.Name} as the rule: {direction.OrientationMeaning.Summary} Review the result before the next step.";
    }

    private static string PositionMeaning(string position, bool thai) =>
        position.ToUpperInvariant() switch
        {
            "PAST" when thai => "ในตำแหน่งอดีต ไพ่ใบนี้อธิบายรากเดิม เหตุสะสม หรือรูปแบบที่ยังส่งผลมาถึงปัจจุบัน",
            "PAST" => "In PAST, this card defines the prior cause, accumulated condition, or pattern still shaping the present.",
            "PRESENT" when thai => "ในตำแหน่งปัจจุบัน ไพ่ใบนี้ชี้พลวัตที่กำลังทำงานและสิ่งที่ต้องมองให้ชัดตอนนี้",
            "PRESENT" => "In PRESENT, this card names the active dynamic and what must be seen clearly now.",
            "DIRECTION" when thai => "ในตำแหน่งทิศทาง ไพ่ใบนี้กำหนดคุณภาพของก้าวถัดไป ไม่ใช่ผลลัพธ์ที่ตายตัว",
            "DIRECTION" => "In DIRECTION, this card defines the quality of the next step, not a fixed outcome.",
            "GUIDANCE" when thai => "ในตำแหน่งคำแนะนำ ไพ่ใบนี้ระบุหลักคิดที่ควรทดลองใช้กับสถานการณ์วันนี้",
            "GUIDANCE" => "In GUIDANCE, this card supplies the principle to test in today's situation.",
            _ when thai => "ตำแหน่งนี้บอกหน้าที่ของไพ่ในลำดับเรื่องและควรอ่านร่วมกับไพ่ใบอื่น",
            _ => "This position defines the card's role in the sequence and should be read with the surrounding cards."
        };

    private static string NarrativeRole(string position, Orientation orientation, bool thai)
    {
        var orientationRole = orientation == Orientation.UPRIGHT
            ? (thai ? "พลังพร้อมแสดงออก" : "available expression")
            : (thai ? "พลังติดขัดหรือทำงานภายใน" : "internalized or blocked expression");

        return thai
            ? $"{PositionLabel(position, true)}: {orientationRole}"
            : $"{PositionLabel(position, false)}: {orientationRole}.";
    }

    private static string DomainMeaning(TarotDomain domain, bool thai) =>
        domain switch
        {
            TarotDomain.LOVE when thai => "ในเรื่องความรัก ให้แปลความผ่านการตอบแทนกัน ความซื่อตรงทางอารมณ์ ความเข้ากันได้ และขอบเขต",
            TarotDomain.LOVE => "For love, read this through reciprocity, emotional honesty, compatibility, and boundaries.",
            TarotDomain.CAREER when thai => "ในเรื่องงาน ให้แปลความผ่านบทบาท ทักษะ อำนาจตัดสินใจ ทิศทาง และผลที่ทำได้จริง",
            TarotDomain.CAREER => "For career, read this through role, skill, agency, direction, and observable results.",
            TarotDomain.MONEY when thai => "ในเรื่องการเงิน ให้แปลความผ่านทรัพยากร ความเสี่ยง กระแสเงิน ความยั่งยืน และข้อตกลง",
            TarotDomain.MONEY => "For money, read this through resources, risk, cash flow, sustainability, and agreements.",
            TarotDomain.FAMILY when thai => "ในเรื่องครอบครัว ให้แปลความผ่านบทบาท การดูแล ความคาดหวังร่วม ขอบเขต และความมั่นคง",
            TarotDomain.FAMILY => "For family, read this through roles, care, shared expectations, boundaries, and security.",
            TarotDomain.PERSONAL_GROWTH when thai => "ในการเติบโตภายใน ให้แปลความผ่านความตระหนัก รูปแบบซ้ำ ความรับผิดชอบ และทางเลือกใหม่",
            TarotDomain.PERSONAL_GROWTH => "For personal growth, read this through awareness, repeated patterns, responsibility, and new choices.",
            _ when thai => "ในภาพรวม ให้แปลความผ่านรูปแบบที่เกิดซ้ำ ทางเลือกที่ควบคุมได้ และผลกระทบต่อชีวิตประจำวัน",
            _ => "In general, read this through recurring patterns, controllable choices, and everyday impact."
        };

    private static string DomainAction(TarotDomain domain, bool thai) =>
        domain switch
        {
            TarotDomain.LOVE => thai ? "การสื่อสาร ความต้องการ และขอบเขตในความสัมพันธ์" : "communication, needs, and boundaries in the relationship",
            TarotDomain.CAREER => thai ? "บทบาทงาน ทักษะ และทิศทางอาชีพ" : "your role, skills, and career direction",
            TarotDomain.MONEY => thai ? "งบประมาณ ความเสี่ยง และการตัดสินใจเรื่องทรัพยากร" : "budget, risk, and resource decisions",
            TarotDomain.FAMILY => thai ? "บทบาท การดูแล และขอบเขตในครอบครัว" : "family roles, care, and boundaries",
            TarotDomain.PERSONAL_GROWTH => thai ? "รูปแบบภายในและทางเลือกที่อยากฝึกใหม่" : "the inner pattern and the new choice you want to practice",
            _ => thai ? "ทางเลือกที่ควบคุมได้ในสถานการณ์นี้" : "the controllable choice in this situation"
        };

    private static string ReflectionQuestion(TarotDomain domain, bool thai) =>
        domain switch
        {
            TarotDomain.LOVE => thai ? "ความสัมพันธ์นี้ต้องการความซื่อตรงหรือขอบเขตข้อใดจากฉันในตอนนี้?" : "What honesty or boundary does this relationship need from me now?",
            TarotDomain.CAREER => thai ? "ก้าวงานข้อใดใช้ทักษะของฉันได้จริงและสอดคล้องกับทิศทางระยะยาว?" : "Which career step uses my real skills and aligns with the longer direction?",
            TarotDomain.MONEY => thai ? "การตัดสินใจใดเพิ่มความมั่นคงโดยไม่ปฏิเสธความเสี่ยงที่มีอยู่จริง?" : "Which decision improves stability without denying the real risk?",
            TarotDomain.FAMILY => thai ? "ฉันดูแลความสัมพันธ์นี้ได้อย่างไรโดยไม่ละทิ้งขอบเขตของตนเอง?" : "How can I care for this relationship without abandoning my own boundaries?",
            TarotDomain.PERSONAL_GROWTH => thai ? "รูปแบบเดิมข้อใดกำลังขอให้ฉันเลือกตอบสนองแบบใหม่?" : "Which old pattern is asking me to choose a new response?",
            _ => thai ? "ส่วนใดของสถานการณ์นี้อยู่ในการควบคุมของฉันและควรลงมือก่อน?" : "What part of this situation is within my control and deserves the first action?"
        };

    private static string ResolveDominantElement(IReadOnlyList<ResolvedCard> cards) =>
        cards.GroupBy(card => card.Meaning.Element, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => Array.FindIndex(new[] { "FIRE", "WATER", "AIR", "EARTH" }, value => string.Equals(value, group.Key, StringComparison.OrdinalIgnoreCase)))
            .First().Key.ToUpperInvariant();

    private static string ElementLens(string element, bool thai) =>
        element.ToUpperInvariant() switch
        {
            "FIRE" => thai ? "แรงขับ ความกล้า และการลงมือ" : "motivation, courage, and action",
            "WATER" => thai ? "อารมณ์ ความสัมพันธ์ และสัญชาตญาณ" : "emotion, relationship, and intuition",
            "AIR" => thai ? "ความคิด การสื่อสาร และการตัดสินใจ" : "thought, communication, and decision",
            _ => thai ? "ทรัพยากร ความมั่นคง และการทำให้เกิดผลจริง" : "resources, stability, and practical execution"
        };

    private static string ElementExcess(string element, bool thai) =>
        element.ToUpperInvariant() switch
        {
            "FIRE" => thai ? "ความรีบร้อนหรือการฝืน" : "haste or force",
            "WATER" => thai ? "อารมณ์ท่วมท้นหรือขอบเขตที่ไม่ชัด" : "emotional flooding or unclear boundaries",
            "AIR" => thai ? "การคิดวนหรือถ้อยคำที่ตัดขาดจากความรู้สึก" : "overthinking or words detached from feeling",
            _ => thai ? "การยึดความปลอดภัยจนไม่ยอมเปลี่ยน" : "holding security so tightly that change becomes impossible"
        };

    private static string LocalizedElement(string element, bool thai)
    {
        if (!thai)
        {
            return element.ToLowerInvariant();
        }

        return element.ToUpperInvariant() switch
        {
            "FIRE" => "ไฟ",
            "WATER" => "น้ำ",
            "AIR" => "ลม",
            _ => "ดิน"
        };
    }

    private static string DomainLabel(TarotDomain domain, bool thai) =>
        domain switch
        {
            TarotDomain.LOVE => thai ? "ความรัก" : "Love",
            TarotDomain.CAREER => thai ? "การงาน" : "Career",
            TarotDomain.MONEY => thai ? "การเงิน" : "Money",
            TarotDomain.FAMILY => thai ? "ครอบครัว" : "Family",
            TarotDomain.PERSONAL_GROWTH => thai ? "การเติบโตภายใน" : "Personal growth",
            _ => thai ? "ภาพรวมชีวิต" : "General life"
        };

    private static string PositionLabel(string position, bool thai) =>
        position.ToUpperInvariant() switch
        {
            "PAST" => thai ? "รากจากอดีต" : "Past root",
            "PRESENT" => thai ? "พลวัตปัจจุบัน" : "Present dynamic",
            "DIRECTION" => thai ? "ทิศทางที่ควรพัฒนา" : "Constructive direction",
            "GUIDANCE" => thai ? "หลักคิดวันนี้" : "Today's principle",
            _ => thai ? "บทบาทในลำดับเรื่อง" : "Role in the sequence"
        };

    private static string FirstKeyword(ResolvedCard card) =>
        card.OrientationMeaning.Keywords.FirstOrDefault() ?? card.OrientationMeaning.Summary;

    private static bool IsThai(string? locale) =>
        locale?.StartsWith("th", StringComparison.OrdinalIgnoreCase) == true;

    private sealed record ResolvedCard(
        SelectedCard Request,
        TarotCardMeaning Meaning,
        LocalizedCardMeaning Localized,
        LocalizedOrientationMeaning OrientationMeaning,
        string PositionMeaning,
        string DomainMeaning);
}
