namespace SapSync.Core.Constants;

/// <summary>
/// SAP IS-H Movement Category (BEWTY) + Movement Type (BWART) composite mapping dictionary.
/// In SAP IS-H, BWART derives its operational clinical meaning ONLY through its combination with BEWTY.
/// This constant class provides the complete mapping for Ziv Medical Center (המרכז הרפואי זיו) operations.
/// </summary>
public static class SapMovementMap
{
    /// <summary>
    /// Complete BEWTY (Movement Category) + BWART (Movement Type) -> Hebrew Description mapping.
    /// Key format: "{BEWTY}|{BWART}" (e.g., "1|01" for Category 1 + Type 01)
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> MovementDescriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ===== CATEGORY 1: קבלה (Admission / Inpatient) - BEWTY = '1' =====
            ["1|01"] = "קבלה רגילה",                           // Standard Admission
            ["1|02"] = "לידה",                                  // Birth / Delivery
            ["1|03"] = "ילוד",                                  // Newborn
            ["1|04"] = "תאונת דרכים",                           // Road Traffic Accident
            ["1|05"] = "תאונת עבודה",                           // Workplace Accident
            ["1|09"] = "חשוד לאחר DRG",                         // Suspected Post-DRG
            ["1|10"] = "לאחר DRG",                              // Post-DRG
            ["1|11"] = "העברה ממרפאות",                         // Transfer from Clinics
            ["1|50"] = "פעולות איבה",                           // Hostile / Terror Action Victim

            // ===== CATEGORY 2: שחרור (Discharge) - BEWTY = '2' =====
            ["2|10"] = "הביתה",                                 // Discharge Home
            ["2|15"] = "טיפול ביתי",                            // Home Care
            ["2|16"] = "אשפוז בית",                             // Home Hospitalization
            ["2|20"] = "למוסד אחר",                             // Transfer to Another Institution
            ["2|30"] = "סירב לקבל טיפול",                       // Refused Medical Treatment
            ["2|40"] = "פטירה",                                 // Deceased
            ["2|50"] = "שוחרר ללא מנהלה",                       // Discharged Without Admin
            ["2|51"] = "עזב עם הגיליון",                        // Left With Medical Record
            ["2|52"] = "עזב על דעת עצמו",                       // Left Against Medical Advice (AMA)
            ["2|60"] = "אחר",                                   // Other Discharge
            ["2|70"] = "התקבל מת",                              // Dead on Arrival (DOA)
            ["2|95"] = "הסבה - ביטול אשפוז",                    // Reclassification - Cancel Admission
            ["2|96"] = "הסבה - העברה למח'",                    // Reclassification - Transfer to Dept
            ["2|97"] = "הסבה - כללי",                           // Reclassification - General
            ["2|98"] = "הסבה - התקבל מת",                       // Reclassification - DOA
            ["2|C4"] = "בטול אשפוז/מיון",                       // Cancel Admission / ER
            ["2|C6"] = "נפגע אר״ן בבי״ח",                       // ARAN Casualty In Hospital

            // ===== CATEGORY 3: העברה (Internal Transfer) - BEWTY = '3' =====
            ["3|10"] = "מעבר חדר",                              // Room Transfer
            ["3|20"] = "מעבר מיטה",                             // Bed Transfer
            ["3|30"] = "מעבר מחלקה - רפוא",                     // Medical Department Transfer
            ["3|40"] = "מעבר מחלקה - תפוס",                     // Occupancy Dept Transfer
            ["3|50"] = "בקשת המשפחה",                           // Family Request Transfer
            ["3|60"] = "ממיון לאשפוז",                          // Transfer from ER to Inpatient Ward

            // ===== CATEGORY 4: ביקור מטופל אמבולטורי (Outpatient Visit) - BEWTY = '4' =====
            ["4|01"] = "ביקור במרפאה",                          // Clinic Visit
            ["4|02"] = "ב. חוזר במרפאה",                        // Clinic Repeat Visit
            ["4|04"] = "תאונת דרכים",                           // Ambulatory Road Accident
            ["4|05"] = "תאונת עבודה",                           // Ambulatory Work Accident
            ["4|06"] = "אשפוז יום",                             // Day Hospitalization
            ["4|07"] = "אחסון גויה",                            // Morgue Storage
            ["4|12"] = "ב. ר. לאחר אשפוז",                      // Post-Hospitalization Repeat Visit
            ["4|13"] = "ב. ר. לאחר מיון",                       // Post-ER Repeat Visit
            ["4|14"] = "טרום ניתוח",                           // Pre-Surgery Assessment
            ["4|15"] = "לאחר אשפוז יום",                        // Post-Day Hospitalization
            ["4|16"] = "לפני אשפוז",                            // Pre-Hospitalization
            ["4|17"] = "ללא נוכחות",                            // Virtual / Remote Administrative Visit
            ["4|18"] = "לפני ניתוח תל״ש",                       // Pre-Elective Surgery
            ["4|19"] = "לאחר ניתוח תל״ש",                       // Post-Elective Surgery
            ["4|20"] = "טלמדיסין",                              // Telemedicine Consult
            ["4|24"] = "ב. ר. טלמד. ל. מיון",                   // Telemedicine ER Repeat Visit
            ["4|25"] = "ב. ר. טלמד. ל. אשפו",                   // Telemedicine Inpatient Repeat
            ["4|26"] = "א. יום טלמדיסין",                       // Telemedicine Day Hospitalization
            ["4|50"] = "פעולות איבה",                           // Ambulatory Terror/Hostile Victim
            ["4|99"] = "במהלך אשפ. - הסבה",                     // Reclassification During Inpatient
            ["4|EO"] = "מקרה חירום אמבולטורי",                  // Emergency Outpatient Encounter
            ["4|OP"] = "ניתוח",                                 // Surgical Procedure

            // ===== CATEGORY 6: תחילת היעדרות (Absence / Leave Start) - BEWTY = '6' =====
            ["6|01"] = "בקשת המטופל",                           // Patient Request Absence
            ["6|02"] = "טיפול / בדיקה",                        // External Test / Medical Leave
            ["6|03"] = "ללא רשות",                              // Absence Without Leave (AWOL)
            ["6|04"] = "סוף שבוע / חג",                         // Weekend / Holiday Leave
            ["6|05"] = "אירוע משפחתי",                          // Family Event Leave
            ["6|06"] = "הלך לאיבוד",                            // Missing Patient
            ["6|07"] = "אחר",                                   // Other Absence
            ["6|08"] = "חופשה ברה״נ",                           // Psychiatric Ward Leave

            // ===== CATEGORY 7: סיום היעדרות (Absence Return / End) - BEWTY = '7' =====
            ["7|01"] = "חזרה מתוכננת",                          // Scheduled Return from Absence
            ["7|02"] = "שינוי מצב רפואי",                       // Medical Condition Change Upon Return
            ["7|03"] = "חזר דרך מיון",                          // Returned via Emergency Dept
            ["7|04"] = "חזר על פי דרישה",                       // Returned on Hospital Demand
            ["7|05"] = "לא אותר - שוחרר",                       // Unlocated - Auto Discharged
            ["7|06"] = "מסרב לחזור - שוחרר",                    // Refused Return - Auto Discharged
            ["7|07"] = "נפטר",                                  // Deceased While on Leave
            ["7|08"] = "אחר",                                   // Other Return
        };

    /// <summary>
    /// Patient Type / Billing Classification (CLASS) codes to Hebrew descriptions.
    /// Used in N_FALL_BEW and PATIENT_MONITORING.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> PatientTypeDescriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["1"]  = "אזרח",                                    // Citizen
            ["2"]  = "צה״ל",                                    // IDF Soldier
            ["4"]  = "תושב חוץ",                                // Foreign Resident
            ["5"]  = "שטחים",                                   // Palestinian Territory Resident
            ["7"]  = "עובד זר",                                 // Foreign Worker
            ["8"]  = "ילדי עובד זר",                            // Foreign Worker's Child
            ["9"]  = "תיירות מרפא",                             // Medical Tourist
            ["10"] = "או״ם",                                    // UN Personnel
            ["11"] = "משהב״ט",                                  // Military Medical Service
            ["12"] = "דייר רחוב",                               // Homeless
            ["13"] = "עובד זר עם אשרה",                        // Foreign Worker with Permit
            ["14"] = "שבוי",                                    // Prisoner
            ["15"] = "תושב ארעי",                               // Temporary Resident
            ["16"] = "פליט",                                    // Refugee
            ["17"] = "שוטר",                                    // Police Officer
            ["99"] = "אחר",                                     // Other
        };

    /// <summary>
    /// Case Type (FALAR) codes to Hebrew descriptions.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CaseTypeDescriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["1"] = "אשפוז",                                    // Inpatient
            ["2"] = "אמבולטורי",                                // Outpatient
            ["3"] = "מיון",                                     // Emergency / ER
        };

    /// <summary>
    /// Triage Status levels used in PATIENT_MONITORING for ARAN (Multi-Casualty Incident).
    /// </summary>
    public static class TriageStatuses
    {
        public const string Red = "RED";           // Critical / Priority 1 (דחוף)
        public const string Yellow = "YELLOW";     // Moderate / Priority 2 (בינוני)
        public const string Green = "GREEN";       // Minor / Priority 3 (קל)
        public const string Black = "BLACK";       // Deceased / Expectant (מוות)
    }

    /// <summary>
    /// Try to resolve a movement description from BEWTY and BWART.
    /// </summary>
    /// <param name="bewty">Movement Category (1-digit)</param>
    /// <param name="bwart">Movement Type (2-4 digit, padded to 2 chars minimum)</param>
    /// <returns>Hebrew description or null if not found</returns>
    public static string? TryGetMovementDescription(string? bewty, string? bwart)
    {
        if (string.IsNullOrWhiteSpace(bewty) || string.IsNullOrWhiteSpace(bwart))
            return null;

        var key = $"{bewty.Trim()}|{bwart.Trim()}";
        return MovementDescriptions.TryGetValue(key, out var description) ? description : null;
    }

    /// <summary>
    /// Try to resolve a patient type description from CLASS code.
    /// </summary>
    public static string? TryGetPatientType(string? classCode)
    {
        if (string.IsNullOrWhiteSpace(classCode))
            return null;

        return PatientTypeDescriptions.TryGetValue(classCode.Trim(), out var description) ? description : null;
    }

    /// <summary>
    /// Try to resolve a case type description from FALAR code.
    /// </summary>
    public static string? TryGetCaseType(string? falar)
    {
        if (string.IsNullOrWhiteSpace(falar))
            return null;

        return CaseTypeDescriptions.TryGetValue(falar.Trim(), out var description) ? description : null;
    }
}
