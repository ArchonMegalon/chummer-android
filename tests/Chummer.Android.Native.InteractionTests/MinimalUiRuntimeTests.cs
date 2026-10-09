using System.Reflection;
using System.Globalization;
using System.Text.RegularExpressions;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    // Short effect summaries, not complete tabletop procedures or legal clearance.
    private static readonly (string Name, string Numbers, string[] English, string[] German, string[] Spanish)[] BriefDrawbackSummaries =
    [
        ("Addiction (Mild)", "-2,-2", ["Monthly", "unresisted withdrawal", "mental-based tests", "psychological dependence", "physical-based tests", "physical dependence", "No persistent social penalty"],
            ["Monatliches", "nicht widerstandenem Entzug", "geistig basierte Proben", "psychischer", "körperlich basierte", "körperlicher Abhängigkeit", "Kein dauerhafter Sozialabzug"],
            ["Ansia mensual", "abstinencia no resistida", "pruebas basadas en atributos mentales", "dependencia psicológica", "basadas en atributos físicos", "dependencia física", "Sin penalización social permanente"]),
        ("Addiction (Moderate)", "-4,-4", ["Fortnightly", "unresisted withdrawal", "mental-based tests", "psychological dependence", "physical-based tests", "physical dependence", "No persistent social penalty"],
            ["Zweiwöchentliches", "nicht widerstandenem Entzug", "geistig basierte Proben", "psychischer", "körperlich basierte", "körperlicher Abhängigkeit", "Kein dauerhafter Sozialabzug"],
            ["Ansia quincenal", "abstinencia no resistida", "pruebas basadas en atributos mentales", "dependencia psicológica", "basadas en atributos físicos", "dependencia física", "Sin penalización social permanente"]),
        ("Addiction (Severe)", "-4,-4,-2", ["Weekly", "unresisted withdrawal", "mental-based tests", "psychological dependence", "physical-based tests", "physical dependence", "Social tests always"],
            ["Wöchentliches", "nicht widerstandenem Entzug", "geistig basierte Proben", "psychischer", "körperlich basierte", "körperlicher Abhängigkeit", "Sozialproben immer"],
            ["Ansia semanal", "abstinencia no resistida", "pruebas basadas en atributos mentales", "dependencia psicológica", "basadas en atributos físicos", "dependencia física", "Pruebas sociales siempre"]),
        ("Addiction (Burnout)", "-6,-6,-3", ["Daily", "unresisted withdrawal", "mental-based tests", "psychological dependence", "physical-based tests", "physical dependence", "Social tests always"],
            ["Tägliches", "nicht widerstandenem Entzug", "geistig basierte Proben", "psychischer", "körperlich basierte", "körperlicher Abhängigkeit", "Sozialproben immer"],
            ["Ansia diaria", "abstinencia no resistida", "pruebas basadas en atributos mentales", "dependencia psicológica", "basadas en atributos físicos", "dependencia física", "Pruebas sociales siempre"]),
        ("Allergy (Common, Mild)", "-2,-1", ["Physical tests", "resist attacks using it", "no ongoing damage"],
            ["körperliche Proben", "Angriffe mit dem Allergen", "kein fortlaufender Schaden"],
            ["pruebas Físicas", "resistir ataques con él", "sin daño continuo"]),
        ("Allergy (Common, Moderate)", "-4,-2", ["Physical tests", "resist attacks using it", "no ongoing damage"],
            ["körperliche Proben", "Angriffe mit dem Allergen", "kein fortlaufender Schaden"],
            ["pruebas Físicas", "resistir ataques con él", "sin daño continuo"]),
        ("Allergy (Common, Severe)", "-4,-3", ["all tests", "to resist allergen attacks", "ongoing unresisted Physical damage"],
            ["alle Proben", "Widerstand gegen Allergenangriffe", "fortlaufender körperlicher Schaden ohne Widerstand"],
            ["todas las pruebas", "resistencia a sus ataques", "daño Físico continuo sin resistencia"]),
        ("Allergy (Common, Extreme)", "-6,-4", ["actions", "resistance to allergen attacks", "faster unresisted Physical damage", "First Aid, Medicine or magic can stop shock"],
            ["Handlungen", "Widerstand gegen Allergenangriffe", "schneller körperlicher Schaden ohne Widerstand", "Erste Hilfe, Medizin oder Magie können den Schock stoppen"],
            ["acciones", "resistencia a sus ataques", "daño Físico acelerado sin resistencia", "primeros auxilios, Medicina o magia pueden detener el shock"]),
        ("Allergy (Uncommon, Mild)", "-2,-1", ["Physical tests", "resist attacks using it", "no ongoing damage"],
            ["körperliche Proben", "Angriffe mit dem Allergen", "kein fortlaufender Schaden"],
            ["pruebas Físicas", "resistir ataques con él", "sin daño continuo"]),
        ("Allergy (Uncommon, Moderate)", "-4,-2", ["Physical tests", "resist attacks using it", "no ongoing damage"],
            ["körperliche Proben", "Angriffe mit dem Allergen", "kein fortlaufender Schaden"],
            ["pruebas Físicas", "resistir ataques con él", "sin daño continuo"]),
        ("Allergy (Uncommon, Severe)", "-4,-3", ["all tests", "to resist allergen attacks", "ongoing unresisted Physical damage"],
            ["alle Proben", "Widerstand gegen Allergenangriffe", "fortlaufender körperlicher Schaden ohne Widerstand"],
            ["todas las pruebas", "resistencia a sus ataques", "daño Físico continuo sin resistencia"]),
        ("Allergy (Uncommon, Extreme)", "-6,-4", ["actions", "resistance to allergen attacks", "faster unresisted Physical damage", "First Aid, Medicine or magic can stop shock"],
            ["Handlungen", "Widerstand gegen Allergenangriffe", "schneller körperlicher Schaden ohne Widerstand", "Erste Hilfe, Medizin oder Magie können den Schock stoppen"],
            ["acciones", "resistencia a sus ataques", "daño Físico acelerado sin resistencia", "primeros auxilios, Medicina o magia pueden detener el shock"]),
        ("Prejudiced (Common, Biased)", "-2,+2", ["Bias", "common group", "social tests with its members", "their Negotiation against you", "Other groups are unaffected"],
            ["Vorurteil", "häufige Gruppe", "Sozialproben mit ihren Mitgliedern", "deren Verhandlungen mit dir", "Andere Gruppen bleiben unbeeinflusst"],
            ["Sesgo", "grupo frecuente", "pruebas sociales con sus miembros", "sus negociaciones contigo", "Otros grupos no cambian"]),
        ("Prejudiced (Common, Outspoken)", "-4,+4", ["Open prejudice", "common group", "social tests with its members", "their Negotiation against you", "Other groups are unaffected"],
            ["Offenes Vorurteil", "häufige Gruppe", "Sozialproben mit ihren Mitgliedern", "deren Verhandlungen mit dir", "Andere Gruppen bleiben unbeeinflusst"],
            ["Prejuicio manifiesto", "grupo frecuente", "pruebas sociales con sus miembros", "sus negociaciones contigo", "Otros grupos no cambian"]),
        ("Prejudiced (Common, Radical)", "-6,+6", ["Radical hostility", "common group", "social tests with its members", "their Negotiation against you", "Other groups are unaffected"],
            ["Radikale Feindseligkeit", "häufige Gruppe", "Sozialproben mit ihren Mitgliedern", "deren Verhandlungen mit dir", "Andere Gruppen bleiben unbeeinflusst"],
            ["Hostilidad radical", "grupo frecuente", "pruebas sociales con sus miembros", "sus negociaciones contigo", "Otros grupos no cambian"]),
        ("Prejudiced (Specific, Biased)", "-2,+2", ["Bias", "narrowly defined group", "social tests with its members", "their Negotiation against you", "Other groups are unaffected"],
            ["Vorurteil", "eng eingegrenzte Gruppe", "Sozialproben mit ihren Mitgliedern", "deren Verhandlungen mit dir", "Andere Gruppen bleiben unbeeinflusst"],
            ["Sesgo", "grupo específico", "pruebas sociales con sus miembros", "sus negociaciones contigo", "Otros grupos no cambian"]),
        ("Prejudiced (Specific, Outspoken)", "-4,+4", ["Open prejudice", "narrowly defined group", "social tests with its members", "their Negotiation against you", "Other groups are unaffected"],
            ["Offenes Vorurteil", "eng eingegrenzte Gruppe", "Sozialproben mit ihren Mitgliedern", "deren Verhandlungen mit dir", "Andere Gruppen bleiben unbeeinflusst"],
            ["Prejuicio manifiesto", "grupo específico", "pruebas sociales con sus miembros", "sus negociaciones contigo", "Otros grupos no cambian"]),
        ("Prejudiced (Specific, Radical)", "-6,+6", ["Radical hostility", "narrowly defined group", "social tests with its members", "their Negotiation against you", "Other groups are unaffected"],
            ["Radikale Feindseligkeit", "eng eingegrenzte Gruppe", "Sozialproben mit ihren Mitgliedern", "deren Verhandlungen mit dir", "Andere Gruppen bleiben unbeeinflusst"],
            ["Hostilidad radical", "grupo específico", "pruebas sociales con sus miembros", "sus negociaciones contigo", "Otros grupos no cambian"]),
        ("SINner (National)", "15", ["National SIN", "gross-income tax", "mandatory broadcast", "police access", "identity and biometrics", "false identities do not erase"],
            ["Nationale SIN", "Bruttoeinkommen", "Sendepflicht", "Behördenzugriff", "Identität und Biometrie", "falsche Identitäten löschen"],
            ["SIN nacional", "ingresos brutos", "emisión obligatoria", "acceso policial", "identidad y biometría", "identidades falsas no borran"]),
        ("SINner (Criminal)", "15", ["replaces your former identity", "mandatory broadcast", "gross-income tax", "police scrutiny", "restricted employment and access"],
            ["ersetzt deine alte Identität", "Sendepflicht", "Bruttoeinkommen", "Polizeikontrollen", "eingeschränkter Zugang zu Arbeit"],
            ["sustituye tu identidad anterior", "emisión obligatoria", "ingresos brutos", "vigilancia policial", "restricciones laborales y de acceso"]),
        ("SINner (Corporate Limited)", "20", ["Limited corporate SIN", "gross-income tax", "extraction risk", "distrust", "without leadership privileges"],
            ["Eingeschränkte Konzern-SIN", "Bruttoeinkommen", "Extraktionsrisiko", "Misstrauen", "ohne Führungsprivilegien"],
            ["SIN corporativa limitada", "ingresos brutos", "riesgo de extracción", "desconfianza", "sin privilegios directivos"]),
        ("SINner (Corporate)", "10", ["Full corporate SIN", "gross-income tax", "corporate records", "hostility in the shadows", "no free corporate resources"],
            ["Volle Konzern-SIN", "Bruttoeinkommen", "Konzerneinträge", "Feindseligkeit in den Schatten", "keine kostenlosen Konzernressourcen"],
            ["SIN corporativa plena", "ingresos brutos", "registros corporativos", "hostilidad en las sombras", "sin recursos corporativos gratuitos"]),
    ];

    private static readonly (string Name, string English, string German, string Spanish)[] SourceEffectQualitySummaries =
    [
        ("Community Connection", "one Low or Squatter", "einen Unterschicht- oder Squatter", "un estilo de vida bajo o de ocupa"),
        ("Nasty Trog", "neither orks nor trolls", "weder Orks noch Trolle", "no sean orkos ni trolls"),
        ("Trog Artisan", "specifically intended", "speziell", "específicamente"),
        ("Trog Historian", "significant events", "bedeutende Ereignisse", "acontecimientos importantes"),
        ("Trog Leader", "every member", "alle Mitglieder", "todos sus miembros"),
        ("Trog Networker", "the majority", "überwiegend", "predominan"),
        ("Bad Credit", "during your career", "während der Karriere", "durante la carrera"),
        ("Corporate Pariah I", "with its members", "mit seinen Angehörigen", "con sus miembros"),
        ("Corporate Pariah II", "former co-workers or its Johnsons", "früheren Kollegen oder seinen Johnsons", "antiguos compañeros de trabajo o sus Johnsons"),
        ("Basement Dweller", "for the first time", "ersten Kennenlernen", "por primera vez"),
        ("Malware Infection", "Matrix Perception", "Matrixwahrnehmung", "Percepción de la Matriz"),
        ("'Ware Intolerance", "Cyberware and bioware", "Cyberware und Bioware", "ciberware y el bioware"),
        ("Rabble Rouser", "at least ten", "mindestens zehn", "al menos diez"),
        ("Social Chameleon", "Etiquette", "Etikette", "Etiqueta"),
        ("Resonant Discordance", "hot-sim", "Hot-Sim", "hot-sim"),
        ("Special Modifications", "neither Magic nor Resonance", "weder Magie noch Resonanz", "no puedes tener Magia ni Resonancia"),
        ("Special Modifications (Prototype Materials)", "with Prototype Materials", "mit Prototype Materials", "con Prototype Materials"),
        ("Natural Weapon: Kick (Centaur)", "kick", "Tritt", "patada"),
        ("Natural Weapon: Bite (Naga)", "bite", "Biss", "mordisco"),
        ("Claws", "digging", "Grabklauen", "excavadoras"),
        ("Razor Claws", "claws", "Klauen", "garras"),
        ("Retractable Claws", "Retractable", "Einziehbare", "retráctiles"),
        ("Fangs", "bite", "Biss", "mordisco"),
        ("Functional Tail (Thagomizer)", "tail", "Schwanz", "cola"),
        ("Goring Horns", "horns", "Hörner", "cuernos"),
        ("Larger Tusks", "tusks", "Hauer", "colmillos"),
        ("Liar", "social", "soziale", "sociales"),
        ("Quasimodo", "outside the Matrix", "außerhalb der Matrix", "fuera de la Matriz"),
        ("Designated Omega", "Leadership", "Führung", "Liderazgo"),
        ("Ugly And Doesn't Care", "minimum and maximum", "Mindestwert", "mínimo"),
        ("Chatty", "AR or VR", "AR oder VR", "RA o RV"),
        ("Redundancy", "AI", "KI", "IA"),
        ("Fragmentation", "maximum Essence", "maximale Essenz", "Esencia máxima"),
        ("Exceptional Entity", "one chosen mental attribute", "gewählten geistigen Attributs", "atributo mental elegido"),
        ("Death Dealer", "more Drain", "mehr Entzug", "más Drenaje"),
        ("Crystalline Reflexes", "defense", "Verteidigungsproben", "defensa"),
        ("Crystalline Vision", "Assensing", "Askennen", "Percepción Astral"),
        ("Seer", "Psychometry and Sensing", "Psychometrie und Sensibilisierung", "Psicometría y Sensibilidad"),
        ("Null Wizard", "loses Binding", "sperrt Binden", "pierde Vinculación"),
        ("Resonant Stream: Machinist", "Selected complex forms", "Bestimmte komplexe Formen", "Ciertas formas complejas"),
        ("Resonant Stream: Sourceror", "Sourcerer Daemon", "Sourcerer Daemon", "Sourcerer Daemon"),
        ("Quadriplegic", "to zero", "auf null", "en cero"),
        ("Black Forest Native", "Black Forest", "Schwarzwald", "Selva Negra"),
        ("Crystal Breath", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Eye (One Eye)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Eye (Two Eyes)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Liver)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Kidneys)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Stomach)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Intestines)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Liver, Kidneys)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Liver, Stomach)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Liver, Intestines)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Kidneys, Stomach)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Kidneys, Intestines)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Stomach, Intestines)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Liver, Kidneys, Stomach)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Liver, Kidneys, Intestines)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Liver, Stomach, Intestines)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Kidneys, Stomach, Intestines)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Gut (Liver, Kidneys, Stomach, Intestines)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Jaw", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Limb (Arm)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Limb (Leg)", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("Crystal Spine", "without reducing Magic", "ohne Magie zu senken", "sin reducir la Magia"),
        ("The Artisan's Way", "selected skills", "bestimmte Fertigkeiten", "ciertas habilidades"),
        ("The Artist's Way", "improve Artisan", "für Kunsthandwerk", "mejoran Artesanía"),
        ("The Athlete's Way", "athletic skills", "athletische Fertigkeiten", "habilidades atléticas"),
        ("The Invisible Way", "selected stealth, movement and awareness", "bestimmter Qi-Foki", "ciertos focos Qi"),
        ("The Speaker's Way", "social skills", "soziale Fertigkeiten", "habilidades sociales"),
        ("The Warrior's Way", "weapon foci", "Waffenfoki", "focos de arma")
    ];

    // Editorial checks protect concise, useful help; they do not establish copyright clearance.
    private static readonly (string Name, string[] English, string[] German, string[] Spanish)[] ConciseQualitySummaries =
    [
        ("Pilot Origins", ["chosen vehicle class", "without"], ["gewählte Fahrzeugklasse", "ohne"],
            ["clase elegida", "sin"]),
        ("Blood Necromancer", ["both"], ["Zaubernde"], ["paciente"]),
        ("Chakra Interrupter", ["temporarily"], ["vorübergehend"], ["temporalmente"]),
        ("Close Combat Mage", ["choose"], ["Wähle"], ["elige"]),
        ("Dark Ally", ["Restless"], ["ruhelos"], ["inquieto"]),
        ("Arcology Tantrum", ["composure"], ["Selbstbeherrschung"], ["Compostura"]),
        ("People's SIN", ["limited", "tax"], ["begrenzter", "Einkommensteuer"],
            ["limitado", "impuestos"]),
        ("People's SIN (Criminal)", ["criminal records"], ["Vorstrafen"], ["antecedentes"]),
        ("Elemental Attunement", ["unavoidable", "each time"], ["unvermeidbaren", "jedes Mal"],
            ["inevitable", "cada vez"]),
        ("Decaying Dissonance", ["composure"], ["Selbstbeherrschung"], ["Compostura"]),
        ("Nerdrage", ["every"], ["jedes"], ["cada"]),
        ("Prank Warrior", ["session"], ["Spielsitzung"], ["sesión"]),
        ("Wanted by GOD", ["always"], ["stets"], ["siempre"]),
        ("Spiritual Lodge", ["afterward"], ["anschließend"], ["después"]),
        ("Sprawl Tamer", ["always"], ["stets"], ["siempre"]),
        ("Crystalline Diver", ["cold"], ["Kälte"], ["presión/frío"]),
        ("Crystalline Grace", ["both"], ["zwei"], ["ambas"]),
        ("Busted Cyberware", ["no benefit", "Essence", "expensive"],
            ["keinen Nutzen", "Essenz", "teuer"], ["no aporta ventajas", "Esencia", "caro"]),
        ("Designer", ["home device", "Data Processing/Pilot", "Noise"],
            ["Heimatgerät", "Datenverarbeitung/Pilot", "Rauschen"], ["hogar", "Datos/Piloto", "Ruido"]),
        ("Hello World!", ["Each level", "Essence loss"],
            ["Jede Stufe", "Essenzverlust"], ["Cada nivel", "Esencia"]),
        ("Persnickety Renter", ["Only", "chosen device category", "home"],
            ["Nur", "gewählten Kategorie", "Heimatgerät"], ["Solo", "categoría elegida", "hogar"]),
        ("Real World Naiveté", ["physical reality", "penalties", "GM"],
            ["physischen Welt", "Abzüge", "Spielleitung"], ["mundo físico", "penalizar", "director"]),
        ("Charlatan", ["Stage tricks", "Assensing", "briefly afterward"],
            ["Bühnentricks", "Askennen", "kurz danach"], ["escénicos", "Astral", "brevemente después"]),
        ("Chosen Follower", ["mentor", "seasonal", "annually"],
            ["Schutzgeist", "saisonal", "jährlich"], ["mentor", "estacional", "anualmente"]),
        ("Vexcraft", ["visible foci", "greater skill", "owners"],
            ["sichtbare Foki", "höherer Fertigkeit", "Besitzern"], ["focos visibles", "mayor habilidad", "dueños"]),
        ("Hard Luck", ["next tier", "without improving"],
            ["nächste Stufe", "ohne", "verbessern"], ["siguiente nivel", "sin mejorar"]),
        ("Hair Trigger", ["Free Action", "cold-sim", "echo", "control rig", "Simple Action"],
            ["Freie Handlung", "Cold-Sim", "Echo", "Kontrollrig", "Einfache Handlung"],
            ["acción gratuita", "cold-sim", "eco", "interfaz de control", "acción simple"]),
        ("Shoot First, Don't Ask Questions", ["Successful", "Surprise", "briefly", "quick-draw"],
            ["Bestandene", "Überraschungsproben", "kurzzeitig", "schnellziehen"],
            ["Superar", "Sorpresa", "brevemente", "desenfundar"]),
        ("Sapper", ["AI", "bonus dice", "Format Device"],
            ["KI", "Bonuswürfel", "Gerät-formatieren"], ["IA", "dados", "Formatear Dispositivo"]),
        ("Sensor Upgrade", ["sensors", "host device", "slaved"],
            ["Sensoren", "KI-Wirtsgeräts", "untergeordneten"], ["sensores", "aloja", "subordinados"]),
        ("Snooper", ["AI", "bonus dice", "Snoop", "Jam Signals"],
            ["KI-Bonuswürfel", "Schnüffeln", "Signal stören"],
            ["IA", "dados", "Espiar", "Interferir Señales"]),
        ("Virtual Stability", ["No", "Virtual Machine", "surcharge", "normal Matrix damage still"],
            ["Virtuelle Maschine", "keinen Zusatzschaden", "normaler Matrixschaden bleibt"],
            ["Máquina Virtual", "no causa daño adicional", "daño matricial normal sigue"]),
        ("Easily Exploitable", ["loses", "Firewall optimization", "multiple marks", "smaller penalties"],
            ["verliert", "Optimierungsbonus auf Firewall", "mehrere Marken", "leichter"],
            ["pierde", "Firewall", "varias marcas", "menores penalizaciones"]),
        ("Corrupter", ["other programs", "glitches", "more likely"],
            ["häufiger Patzer", "anderen Programmen"], ["más fallos", "otros programas"]),
        ("The Twisted Way", ["toxic", "awakened type", "learned separately"],
            ["toxische", "Begabung", "einzeln erlernt"], ["tóxica", "tipo", "por separado"]),
        ("Conjuring Geas", ["restriction", "all Conjuring", "bad astral reputation"],
            ["Einschränkung", "aller Beschwörungsfertigkeiten", "schlechten Ruf"],
            ["restricción", "todas", "Conjuración", "mala reputación astral"]),
        ("It Works If You Work It", ["Infected", "addiction", "Essence Drain"],
            ["Infizierten", "Essenzentzug", "Sucht"], ["Infectados", "adicción", "Drenaje de Esencia"]),
        ("Soul Swallower", ["Essence faster", "addiction risk", "normal rate", "without"],
            ["Schnellerer Essenzentzug", "Suchtrisiko", "normalen Tempo", "entfällt"],
            ["Esencia más rápido", "riesgo de adicción", "ritmo normal", "evita"]),
        ("Metaviral Attunement", ["specific", "spells, spirits or other tests", "strain"],
            ["bestimmte", "Zauber, Geister oder andere Proben", "Virusstamm"],
            ["determinadas", "hechizos, espíritus u otras pruebas", "cepa"]),
        ("Stalwart Ally", ["Edge", "once daily between you", "next dawn/dusk", "Drain resistance"],
            ["Edge", "gemeinsam einmal täglich", "nächsten Sonnenaufgang/-untergang", "Entzugswiderstand"],
            ["Edge", "una vez diaria en total", "próximo amanecer/anochecer", "resistir Drenaje"]),
        ("Taboo Transformer", ["Resisted", "Shapechange or Critter Form", "unwilling", "physically weakened", "mental attributes stay unchanged"],
            ["Widerstandsprobe", "Gestaltwandlung oder Tiergestalt", "widerstrebende", "körperlich geschwächte", "geistigen Attribute bleiben unverändert"],
            ["prueba resistida", "Cambio de Forma o Forma Animal", "reacios", "físicamente debilitados", "atributos mentales no cambian"]),
        ("Worship Leader", ["Voluntary", "mundane", "your tradition", "dice pool and limit", "capped"],
            ["Freiwillig", "mundane", "deiner Tradition", "Würfelpool und Limit", "begrenzt"],
            ["voluntariamente", "mundanos", "tu tradición", "dados y el límite", "limita"]),
        ("Spirit Hunter I", ["owed services", "Banishing", "Astral Combat/Killing Hands", "briefly"],
            ["Diensten", "Astralkampf", "Todeskralle", "kurzzeitig"],
            ["servicios", "Destierro", "combate astral", "Manos Letales", "brevemente"]),
        ("Spirit Hunter II", ["owed services", "Banishing", "Astral Combat/Killing Hands", "longer"],
            ["Diensten", "Astralkampf", "Todeskralle", "länger"],
            ["servicios", "Destierro", "combate astral", "Manos Letales", "más tiempo"]),
        ("Spirit Hunter III", ["owed services", "Banishing", "Astral Combat/Killing Hands", "longest"],
            ["Diensten", "Astralkampf", "Todeskralle", "am längsten"],
            ["servicios", "Destierro", "combate astral", "Manos Letales", "más tiempo aún"]),
        ("Spiritual Pilgrim", ["background count", "more quickly"],
            ["Hintergrundstrahlung", "schneller"], ["trasfondo astral", "más rápido"]),
        ("Improved Restoration", ["Successful", "extra damage", "Core Condition Monitor"],
            ["Erfolgreiche", "zusätzlichen Schaden", "Kern-Zustandsmonitor"],
            ["exitosas", "daño adicional", "monitor del núcleo"]),
        ("Low Profile", ["devices", "Overwatch", "more slowly", "Emulate", "no reduction"],
            ["Geräte", "Overwatch", "langsamer", "Emulieren", "keine"],
            ["dispositivos", "Vigilancia", "lentamente", "Emular", "no"]),
        ("Munge", ["Essence", "Matrix entities", "code", "physical or astral", "ineligible"],
            ["Essenz", "Matrixwesen", "Codeverzehr", "körperliche oder astrale", "keine"],
            ["Esencia", "Matriz", "código", "físicos o astrales", "no"]),
        ("Multiprocessing", ["Observe in Detail", "unopposed Matrix Perception", "Free Actions", "outside", "combat"],
            ["Genaues Beobachten", "nicht vergleichende Matrixwahrnehmung", "Freie Handlungen", "außerhalb", "kämpfen"],
            ["Observar en Detalle", "Percepción Matricial no enfrentada", "gratuitas", "fuera", "combate"]),
        ("Bi-Polar", ["Agility/Reaction", "Logic/Intuition", "depression", "stability", "paid medication"],
            ["GES/REA", "LOG/INT", "Depression", "Stabile", "Medikamente", "kosten"],
            ["Agilidad/Reacción", "Lógica/Intuición", "depresión", "Estabilidad", "medicación", "pago"]),
        ("Centaur Body", ["anatomy", "inherited", "not", "purchased"],
            ["Körperbau", "angeborene", "kein", "gekaufter"],
            ["anatomía", "innato", "no", "comprada"]),
        ("Latent Dracomorphosis", ["No immediate", "GM", "Karma debt", "Resonance", "Magic"],
            ["Zunächst keine", "Spielleiter", "Karmaschulden", "Resonanz", "Magie"],
            ["Sin", "inmediatos", "director", "deuda de Karma", "Resonancia", "Magia"]),
        ("Dissonant Stream: Apophenian", ["Device", "less Fading", "Compiling/Decompiling", "Data or Generalist", "Submersion", "Sleaze"],
            ["Gerätebezogene", "weniger Schwund", "Daten-/Generalisten", "kompilierst/dekompilierst", "Wandlung", "Schleicher"],
            ["dispositivos", "Menos Desvanecimiento", "Compilar/Descompilar", "datos o generalistas", "Sumersión", "Sigilo"]),
        ("Dissonant Stream: Erisian", ["other personas", "less Fading", "Compiling/Decompiling", "Crack or Generalist", "Submersion", "Firewall"],
            ["andere Personas", "weniger Schwund", "Infiltrator-/Generalisten", "kompilierst/dekompilierst", "Wandlung", "Firewall"],
            ["otras personas", "Menos Desvanecimiento", "Compilar/Descompilar", "intrusión o generalistas", "Sumersión", "Firewall"]),
        ("Dissonant Stream: Morphinae", ["host/IC attributes", "less Fading", "Compiling/Decompiling", "Fault or Generalist", "Submersion", "Noise"],
            ["Host-/IC-Attribute", "weniger Schwund", "Stör-/Generalisten", "kompilierst/dekompilierst", "Wandlung", "Rauschen"],
            ["atributos de hosts/IC", "Menos Desvanecimiento", "Compilar/Descompilar", "fallo o generalistas", "Sumersión", "Ruido"]),
        ("Revenant Adept", ["Regeneration", "yearly", "seasonal", "month"],
            ["Regeneration", "jährlichen", "saisonalen", "Monat"],
            ["Regeneración", "anuales", "estacional", "mes"]),
        ("Skinwalker", ["Critter", "each rank", "sizes", "self-transformation", "hide"],
            ["eigener Tiergestalt", "jede Stufe", "Tiergrößen", "Tierhaut"],
            ["Forma Animal", "cada grado", "tamaños", "ti mismo", "piel"]),
        ("Spell Jammer", ["Counterspelling", "visible", "temporarily", "Spellcasting"],
            ["Antimagie", "sichtbare", "Spruchzaubereiproben", "vorübergehend"],
            ["Contraconjuros", "visibles", "temporalmente", "Lanzamiento de hechizos"]),
        ("Code of Honor: Black Hat", ["data", "sell", "pays most", "never"],
            ["Daten", "Meistbietende", "verschenke", "niemals"],
            ["datos", "pague más", "nunca", "regales"]),
        ("Know Your Limit", ["Physical", "harder", "Stun", "unaffected"],
            ["Körperlichem", "schwieriger", "Geistiger", "unverändert"],
            ["físico", "difícil", "aturdimiento", "no cambia"]),
        ("Sprite Combustion", ["fewer tasks", "at least one", "Registering", "penalty"],
            ["weniger", "mindestens einen Dienst", "Registrieren", "Würfelabzug"],
            ["menos tareas", "al menos una", "Registrar", "penalizador"]),
        ("Taint of Dissonance", ["Resonance entities", "lower limit", "not", "technomancers"],
            ["Resonanzwesen", "niedrigeres Limit", "Technomancer", "nicht"],
            ["Resonancia", "límites menores", "tecnomantes", "no"]),
        ("Wired User", ["sober", "Matrix", "penalty"],
            ["nüchtern", "Matrixhandlungen", "Würfelabzug"],
            ["sobrio", "dados", "Matriz"]),
        ("Electronic Witness", ["sound", "video", "wireless off", "all actions"],
            ["Ton", "Bild", "abgeschalteter Funk", "alle Handlungen"],
            ["audio", "vídeo", "apagar", "todas las acciones"]),
        ("Faraday Himself", ["noise", "nearby", "yourself", "not distant"],
            ["Rauschen", "dir", "nahen Nutzern", "außerhalb"],
            ["ruido", "ti", "cercanos", "fuera"]),
        ("Latest and Greatest", ["monthly", "most earnings", "earmarked", "other expenses"],
            ["monatlich", "Großteil", "vorgemerktes", "unbenutzbar"],
            ["mensualmente", "mayoría", "ahorro", "otros gastos"]),
        ("Leeeeeeeroy Jenkins", ["Failed Composure", "immediate", "delays retreat", "name"],
            ["Misslungene Selbstbeherrschung", "sofortige", "Rückzug", "Name"],
            ["Compostura", "inmediatamente", "retirarse", "nombre"]),
        ("Spectral Warden", ["Only summon", "Binding", "more optional spirit powers", "Minion", "atonement"],
            ["Beschwören nur durch Binden", "optionale Geisterkräfte", "Dienerrituale", "Buße"],
            ["solo", "Vincular", "poderes", "Esbirro", "expiar"]),
        ("Every Man For Himself", ["ally", "Composure", "helping", "danger"],
            ["Verbündeter", "Selbstbeherrschung", "Hilfe", "Gefahr"],
            ["aliado", "Compostura", "ayudar", "peligro"]),
        ("No Man Left Behind", ["Composure", "recover", "dead", "danger"],
            ["Selbstbeherrschung", "bergen", "Gefahr", "tot"],
            ["Compostura", "rescatar", "muertos", "riesgo"]),
        ("Stay Out of My Way", ["Social", "direct superiors", "betray", "Composure"],
            ["Soziale", "direkten Vorgesetzten", "Verrat", "Selbstbeherrschung"],
            ["sociales", "superiores directos", "traicionar", "Compostura"]),
        ("Unique Avatar", ["visible", "social", "remember", "identify"],
            ["sichtbare", "soziale Matrixproben", "erinnerbar", "wiedererkennbar"],
            ["visible", "sociales", "recuerden", "identifiquen"]),
        ("Data Hog", ["Overwatch", "convergence", "earlier", "GOD"],
            ["Overwatch", "Konvergenz", "früher", "GOD"],
            ["Overwatch", "convergencia", "antes", "GOD"]),
        ("Escaped Custody", ["Choose", "corporate", "records", "Composure"],
            ["Wähle", "Akten", "Konzern", "Selbstbeherrschung"],
            ["Elige", "corporación", "registros", "Compostura"]),
        ("On the Wagon", ["Matrix", "dice penalty", "not sober"],
            ["nicht nüchtern", "Matrixhandlungen", "Würfelabzug"],
            ["Matriz", "dados", "no estás sobrio"]),
        ("Puppet Master", ["Each rank", "mental manipulation", "without penalty", "above your Magic"],
            ["Jede Stufe", "mentalen Manipulationszauber", "Aufrechterhaltungsabzug", "über deinem Magiewert"],
            ["Cada grado", "manipulación mental", "sin penalizador", "encima de tu Magia"]),
        ("Reckless Spell Master", ["daily", "extra Drain", "ranks", "uninterrupted rest"],
            ["tägliche", "Zusatzentzug", "Stufe", "ununterbrochene Ruhe"],
            ["diaria", "grado", "Drenaje adicional", "descanso ininterrumpido"]),
        ("Renaissance Ritualist", ["Lead", "mixed-tradition", "participant allowance", "Magic", "initiation"],
            ["Leite", "Traditionen", "Teilnehmerzahl", "Magie", "Initiation"],
            ["Dirige", "tradiciones", "cupo", "Magia", "iniciación"]),
        ("Shock Mage", ["Damaging Combat", "initiative", "stacking"],
            ["Kampfzauber", "Schaden", "Initiative", "weiterhin"],
            ["combate", "daño", "iniciativa", "acumulándose"]),
        ("Delicate Fingers", ["handling penalty", "trolls", "gear"],
            ["Trolle", "Bedienungsabzug", "Ausrüstung"],
            ["penalización", "trolls", "equipo"]),
        ("Human Lifespan", ["Creation-only", "ork", "matures", "ages"],
            ["Erschaffung", "Ork", "reift", "altert"],
            ["creación", "orko", "madura", "envejece"]),
        ("Force of Chaos", ["smaller", "combat", "tactics", "group maneuvers"],
            ["geringere", "Kampfboni", "Taktiken", "Gruppenmanövern"],
            ["menores", "combate", "tácticas", "maniobras"]),
        ("Trog Traitor", ["Notoriety", "neighborhoods", "orks", "trolls"],
            ["Schlechter Ruf", "Orks", "Trollen", "Vierteln"],
            ["Notoriedad", "barrios", "orka", "troll"]),
        ("Poor Self Control (Thrill Seeker) (Dareadrenaline)", ["Augmentation", "thrill-seeking", "without Karma", "harder"],
            ["Verstärkung", "Risikodrang", "ohne Karma", "schwieriger"],
            ["mejora", "riesgo", "sin Karma", "más difícil"]),
        ("Rank (Neither Military nor Law Enforcement) I", ["social limits", "members", "chosen organization"],
            ["soziale Limits", "Mitgliedern", "gewählten Organisation"],
            ["límites sociales", "miembros", "organización elegida"]),
        ("Rank (Military or Law Enforcement) I", ["social limits", "internally", "under your authority"],
            ["soziale Limits", "intern", "Amtsgewalt"],
            ["límites sociales", "internos", "bajo tu autoridad"]),
        ("Rank (Neither Military nor Law Enforcement) II", ["social limits", "members", "chosen organization"],
            ["soziale Limits", "Mitgliedern", "gewählten Organisation"],
            ["límites sociales", "miembros", "organización elegida"]),
        ("Rank (Military or Law Enforcement) II", ["social limits", "internally", "under your authority"],
            ["soziale Limits", "intern", "Amtsgewalt"],
            ["límites sociales", "internos", "bajo tu autoridad"]),
        ("Rank (Neither Military nor Law Enforcement) III", ["social limits", "members", "chosen organization"],
            ["soziale Limits", "Mitgliedern", "gewählten Organisation"],
            ["límites sociales", "miembros", "organización elegida"]),
        ("Rank (Military or Law Enforcement) III", ["social limits", "internally", "under your authority"],
            ["soziale Limits", "intern", "Amtsgewalt"],
            ["límites sociales", "internos", "bajo tu autoridad"]),
        ("Location Attunement I", ["Perception", "Surprise", "chosen", "small", "absence"],
            ["Wahrnehmung", "Überraschungsproben", "gewählten", "kleinen", "Abwesenheit"],
            ["Percepción", "Sorpresa", "pequeña", "elegida", "ausencias"]),
        ("Location Attunement II", ["Perception", "Surprise", "chosen", "large home", "small complex", "absence"],
            ["Wahrnehmung", "großen Haus", "kleinen Komplex", "Abwesenheit"],
            ["Percepción", "casa grande", "complejo pequeño", "ausencias"]),
        ("Location Attunement III", ["Perception", "Surprise", "chosen", "large complex", "absence"],
            ["Wahrnehmung", "großen Komplex", "Abwesenheit"],
            ["Percepción", "complejo grande", "ausencias"]),
        ("This Is Your Last Chance", ["corporate", "gamemaster", "dismissal"],
            ["Konzernjob", "Spielleitung", "Entlassung"],
            ["corporativo", "dirección de juego", "despido"]),
        ("Reverberant", ["Non-technomancers", "specifically", "sprites", "Resonance"],
            ["Nicht-Technomancer", "gezielt", "Sprites", "Resonanzwesen"],
            ["no sean", "específicamente", "sprites", "Resonancia"]),
        ("Sprite Affinity", ["Choose", "compiling", "successful", "extra task"],
            ["Wähle", "Kompilieren", "Erfolg", "zusätzliche Aufgabe"],
            ["Elige", "compilarlo", "éxito", "tarea adicional"]),
        ("Trust Data, Not Lore", ["Logic replaces Intuition", "certain", "Perception", "Search"],
            ["Logik ersetzt Intuition", "bestimmten", "Matrixwahrnehmung", "Matrixsuche"],
            ["Lógica sustituye a Intuición", "ciertas", "Percepción", "Búsqueda"]),
        ("Trust Lore, Not Data", ["Intuition replaces Logic", "certain", "Data Spike", "Edit File"],
            ["Intuition ersetzt Logik", "bestimmten", "Datenstachel", "Datei editieren"],
            ["Intuición sustituye a Lógica", "ciertas", "Pincho de Datos", "Editar Archivo"]),
        ("Pacifist Adept", ["Pacifist rank", "attack limits", "living", "peaceful", "by or against", "glitch"],
            ["Pazifistenstufe", "Angriffslimits", "Lebewesen", "friedliche", "von dir oder gegen dich", "patzen"],
            ["grado", "seres vivos", "pacíficas", "tuyos o contra ti", "pifias"]),
        ("Potion Maker", ["Liquid", "basic-trigger", "surcharges", "unless timed", "all liquid"],
            ["Flüssige", "Entzugsaufschlag", "einfacher Auslöser", "Zeitauslösern", "alles"],
            ["líquidas", "básicos", "salvo temporizador", "todo el líquido"]),
        ("Practiced Alchemist", ["potency longer", "activation dice", "initiation"],
            ["länger", "Auslösewürfel", "Initiatengrade"],
            ["más tiempo", "activarse", "iniciación"]),
        ("Corrosive Spit", ["acid", "short range", "Exotic Ranged", "replenishing"],
            ["Säure", "kurze Distanz", "exotischen", "Neubildung"],
            ["ácido", "corta distancia", "Exótica", "regenerar"]),
        ("Defensive Secretion", ["agitated", "penalize", "bare skin", "spirits"],
            ["Aufregung", "Probenabzüge", "Hautkontakt", "Geister"],
            ["alterarte", "pruebas", "piel", "espíritus"]),
        ("Thermal Sensitivity", ["nearby heat", "without sight", "visibility", "interfere"],
            ["nahe Wärme", "ohne Sicht", "Sichtabzüge", "stören"],
            ["calor cercano", "sin ver", "visibilidad", "interferir"]),
        ("Natural Hacker", ["chosen", "instead", "mental"],
            ["gewählten", "Resonanz", "geistige"],
            ["elegida", "Resonancia", "mental"]),
        ("One With the Matrix I", ["join", "authorized", "subordinate"],
            ["Erlaubnis", "untergeordnetes"],
            ["subordinado", "autorización"]),
        ("One With the Matrix II", ["lead", "Resonance", "devices"],
            ["leiten", "Resonanz", "Geräte"],
            ["dirigir", "Resonancia", "dispositivos"]),
        ("One With the Matrix III", ["join", "authorized", "lead", "Resonance"],
            ["erlaubten", "beitreten", "leiten", "Resonanz"],
            ["unirse", "autorizadas", "dirigir", "Resonancia"]),
        ("Missile Deflector", ["catching", "Missile Parry", "Counterstrike", "range"],
            ["Geschossparade", "Gegenangriff-Unterbrechung", "gefangene", "Wurfreichweite"],
            ["atrapar", "Parada", "Contraataque", "alcance"]),
        ("Mystic Foreman", ["Shape", "resistance", "reinforced"],
            ["Formen von Material", "Widerstand", "verstärkte"],
            ["Moldear", "resistencia", "reforzadas"]),
        ("Mystic Pitcher", ["Fling", "range", "called shots"],
            ["Schleuder", "Reichweitenbedingungen", "angesagte"],
            ["Lanzar", "alcance", "localizados"]),
        ("Phenotypic Variation - Shuffle", ["Genetic", "harder", "DNA"],
            ["Gentechnische", "DNA", "schwerer"],
            ["ADN", "dificultad", "genéticas"]),
        ("Phenotypic Variation - Cosmetic Alteration", ["cosmetic", "do not", "limbs"],
            ["kosmetische", "keine", "Gliedmaßen"],
            ["cosméticos", "no añade", "extremidades"]),
        ("Phenotypic Variation - Print Removal", ["skin-ridge", "another"],
            ["Hautleistenabdrücke", "andere"],
            ["huellas", "otro"]),
        ("Phenotypic Variation - Metaposeur", ["chosen", "without", "drawbacks"],
            ["gewählter", "ohne", "Nachteile"],
            ["elegido", "sin", "desventajas"]),
        ("Golden Screwdriver", ["Matrix damage", "simultaneously", "split"],
            ["Matrixschaden", "gleichzeitig", "Aufteilung"],
            ["matricial", "simultáneamente", "repartirlos"]),
        ("Online Fame", ["Recognition", "limits", "distrust"],
            ["Onlinebekanntheit", "Limits", "Misstrauen"],
            ["reconocimiento", "límites", "desconfianza"]),
        ("Pain is Gain", ["Biofeedback", "this Combat Turn", "not"],
            ["Biofeedbackschaden", "dieser Kampfrunde", "nicht"],
            ["biorretroalimentación", "este turno", "no acumulan"]),
        ("Data Liberator", ["freely", "immediately", "payment"],
            ["frei", "bezahlter", "unmittelbar"],
            ["libremente", "cobras", "inmediatamente"]),
        ("Corporate Loyalist", ["Social", "Composure", "chosen", "betray"],
            ["Sozialproben", "Selbstbeherrschung", "gewählten", "Konzernverrat"],
            ["sociales", "Compostura", "elegida", "traicionarla"]),
        ("Items of Power", ["focus addiction", "other purposes"],
            ["Fokusabhängigkeit", "andere Zwecke"],
            ["adicción", "otros fines"]),
        ("Mage Hunter I", ["Combat", "extra Drain"],
            ["Kampfzauber", "Entzug"],
            ["Combate", "Drenaje"]),
        ("Mage Hunter II", ["Further", "Counterspelling", "Drain"],
            ["stärker", "Antimagie", "Entzug"],
            ["más", "Contrahechicería", "Drenaje"]),
        ("Mage Hunter III", ["strongest", "Counterspelling", "Drain"],
            ["stärkste", "Antimagie", "Entzug"],
            ["mayor", "Contrahechicería", "Drenaje"]),
        ("Fractal Punch", ["Data Spike", "Resonance Spike", "trade"],
            ["Datenstachel", "Resonanzstachel", "tauschen"],
            ["datos", "Resonancia", "sacrificar"]),
        ("Lone Wolf", ["initiative", "allies", "own sprites"],
            ["Matrixinitiative", "eigene Sprites", "Verbündete"],
            ["Iniciativa", "aliados", "propios sprites"]),
        ("Team Player", ["Brute Force", "Hack on the Fly", "failure", "cannot"],
            ["Brute Force", "Eiliges Hacken", "Fehlschlagsrisiken", "nicht"],
            ["Fuerza bruta", "Hackeo al vuelo", "riesgos", "excluidos"]),
        ("Phenotypic Variation - Genewipe", ["decay", "ritual", "not immediate"],
            ["zerfallen", "Ritualproben", "nicht sofort"],
            ["rastros", "rituales", "no es inmediata"]),
        ("Phenotypic Variation - Masque", ["no match", "advanced", "other identification"],
            ["keinen Treffer", "Scanner", "andere"],
            ["ADN", "avanzados", "otra identificación"]),
        ("Phenotypic Variation - Reprint", ["new genetic", "old profile", "other"],
            ["genetische", "alten Profil", "anderen"],
            ["genética", "anterior", "otros"]),
        ("Profiler", ["dossier", "without preparation"],
            ["Personendossier", "unvorbereitet"],
            ["expediente", "sin preparación"]),
        ("Quick Config", ["two", "Free Action", "only once"],
            ["zwei", "Freie Handlung", "einmal"],
            ["dos", "Acción gratuita", "una sola vez"]),
        ("Curiosity Killed the Cat", ["gains dice", "Composure", "downloading"],
            ["Bonuswürfel", "Selbstbeherrschung", "herunterzuladen"],
            ["dados", "Compostura", "descargarlos"]),
        ("Animal Pelage (Quills)", ["Exposed", "mobile", "Quills skill"],
            ["freien", "beweglichen", "Waffenfertigkeit"],
            ["descubiertas", "móviles", "arma exótica"]),
        ("Animal Pelage (Camo Fur)", ["dim light", "suitable", "modifications"],
            ["schwachem Licht", "passender Umgebung", "unvereinbar"],
            ["poca luz", "entorno adecuado", "incompatible"]),
        ("Magic Sense", ["Intuition", "Willpower", "without"],
            ["Intuition", "Willenskraft", "ohne"],
            ["Intuición", "Voluntad", "sin"]),
        ("Instinctive Hack", ["Unless surprised", "one opening", "initiative"],
            ["Ohne Überraschung", "ersten", "Kampfinitiative"],
            ["Sin sorpresa", "inicial", "Percepción matricial"]),
        ("Prototype Materials", ["approval", "mundane", "maximum"],
            ["Spielleitungsfreigabe", "mundaner", "Maximum"],
            ["aprobación", "mundano", "máximo"]),
        ("Dry Addict (Mild)", ["abstinence", "Composure", "relapse"],
            ["Abstinenzabzüge", "Selbstbeherrschung", "leicht"],
            ["abstinencia", "Compostura", "ligeramente"]),
        ("Dry Addict (Moderate)", ["Abstinence", "Composure", "greater"],
            ["Abstinenz", "Selbstbeherrschung", "stärker"],
            ["abstinencia", "Compostura", "más"]),
        ("Dry Addict (Severe)", ["abstinence", "Composure", "severe"],
            ["Abstinenz", "Selbstbeherrschung", "hohen"],
            ["abstinencia", "Compostura", "grave"]),
        ("Dry Addict (Burnout)", ["Abstinence", "Composure", "strongest"],
            ["Abstinenz", "Selbstbeherrschung", "stärksten"],
            ["abstinencia", "Compostura", "mayor"]),
        ("Favored (Specific Target, Biased)", ["specific", "negotiating"],
            ["bestimmten", "Verhandlungsposition"],
            ["específico", "negociaciones"]),
        ("Favored (Specific Target, Outspoken)", ["specific", "further"],
            ["bestimmten", "erhöht"],
            ["específico", "empeora"]),
        ("Favored (Specific Target, Fanatic)", ["specific", "greatest"],
            ["bestimmten", "stärkste"],
            ["específico", "maximiza"]),
        ("Flesh Sculpter", ["rank", "Body", "willing"],
            ["Stufe", "Konstitutionsspanne", "freiwilliger"],
            ["nivel", "Constitución", "voluntarios"]),
        ("Healer", ["Health", "net hits", "Essence"],
            ["Nettoerfolge", "Gesundheitszauber", "Essenz"],
            ["éxitos netos", "Salud", "Esencia"]),
        ("Illusionist", ["rank", "Physical", "Mana", "exceed"],
            ["Stufe", "Physisch", "Mana", "übersteigen"],
            ["nivel", "Físico", "Maná", "superar"]),
        ("Brilliant Heuristics", ["Data Processing", "half", "compatible"],
            ["Datenverarbeitung", "halb", "weitere"],
            ["Procesamiento", "mitad", "compatibles"]),
        ("Groveler", ["datachips", "next", "Fading"],
            ["Datenchips", "nächsten", "Schwund"],
            ["chips", "siguiente", "Desvanecimiento"]),
        ("Hold the Door", ["Consecutive", "resets", "other action"],
            ["Aufeinanderfolgend", "verfällt", "anderen"],
            ["consecutivamente", "pierde", "otra acción"]),
        ("One of Them", ["CFD", "fail"],
            ["CFD", "misslungener"],
            ["CFD", "fallen"]),
        ("Poor Self Control (Sadistic)", ["Composure", "jeopardize"],
            ["Selbstbeherrschung", "gefährden"],
            ["Compostura", "arruinar"]),
        ("Tough and Targeted", ["Monthly", "overflow", "Notoriety"],
            ["Monatliche", "Schadensüberlauf", "Schlechten"],
            ["mensuales", "desbordamiento", "Notoriedad"]),
        ("The Goat", ["Strangers", "initial"],
            ["Anfängliches", "Kennenlernen"],
            ["inicial", "desconocidos"]),
        ("Favored (Common Target, Biased)", ["chosen", "negotiations"],
            ["Gruppe", "Verhandlungsposition"],
            ["elegido", "negociaciones"]),
        ("Favored (Common Target, Outspoken)", ["further", "negotiating"],
            ["erhöht", "Verhandlungsvorteil"],
            ["empeora", "negociadora"]),
        ("Favored (Common Target, Fanatic)", ["greatest", "chosen"],
            ["stärkste", "gewählten"],
            ["maximiza", "elegido"]),
        ("Dual-Natured Defender", ["penalties", "permanently"],
            ["Probenabzügen", "dauerhaft"],
            ["penalizaciones", "permanentemente"]),
        ("Durable Preparations", ["longer", "unchanged"],
            ["länger", "unverändert"],
            ["tardan", "sin"]),
        ("Elemental Master", ["chosen", "secondary"],
            ["gewähltes", "Nebeneffekte"],
            ["elegido", "secundarios"]),
        ("Echo Chamber", ["Extended", "glitches"],
            ["Ausgedehnte", "patzen"],
            ["extendidas", "pifias"]),
        ("Frostbite", ["trained", "non-Patrol", "unchanged"],
            ["erlernte", "Patrouillen", "unverändert"],
            ["aprendido", "Patrulla", "no cambia"]),
        ("Information Auctioneer", ["income", "recognize"],
            ["Zusatzeinnahmen", "erkennen"],
            ["ingresos", "reconocer"]),
        ("Better to be Feared Than Loved", ["blackmailed", "retaliation"],
            ["erpresste", "Vergeltung"],
            ["chantaje", "represalia"]),
        ("Revels in Murder", ["unwilling", "danger-aware", "overflow"],
            ["ablehnt", "Gefahr", "Schadensüberlauf"],
            ["no acepta", "peligro", "desbordamiento"]),
        ("Poor Self Control (Attention-Seeking)", ["Composure", "combat"],
            ["Selbstbeherrschung", "Kampf"],
            ["Compostura", "combate"]),
        ("Alpha Junkie", ["Composure", "failure"],
            ["Selbstbeherrschung", "Misslingen"],
            ["Compostura", "fallar"]),
        ("Disheveled", ["clothing", "no"],
            ["Kleidung", "weder"],
            ["ropa", "no"]),
        ("One Born Every Minute", ["resistance", "unaffected"],
            ["widerstehst", "nicht"],
            ["resistir", "sin penalizar"]),
        ("Alchemical Bomb Maker", ["area", "trigger"],
            ["Flächenpräparate", "Auslöser"],
            ["área", "disparador"]),
        ("Arcane Improviser", ["unlearned", "weekly", "Drain"],
            ["ungelernten", "Wochenlimit", "Entzug"],
            ["no aprendido", "semanal", "Drenaje"]),
        ("Archivist", ["Physical", "without"],
            ["Betäubung", "ohne"],
            ["Físico", "sin elevar"]),
        ("Down the Rabbit Hole", ["glitches", "automatic"],
            ["Patzerrisiko", "automatischen"],
            ["pifias", "automáticos"]),
        ("Lazy Fingers", ["multiple", "additional"],
            ["Mehrere", "zusätzliche"],
            ["varias", "adicionales"]),
        ("Matrix Troll", ["Composure", "friends"],
            ["Selbstbeherrschung", "Freunde"],
            ["Compostura", "amigos"]),
        ("Cyberpsychosis", ["social", "temporarily", "gamemaster"],
            ["Soziale", "vorübergehend", "Spielleitung"],
            ["sociales", "temporalmente", "dirección"]),
        ("So Jacked Up", ["Generic", "switching", "unsuitable"],
            ["Standard", "Wechsel", "unpassende"],
            ["genéricos", "cambiar", "inadecuado"]),
        ("TLE-X", ["stress", "failing", "minutes"],
            ["Stress", "misslungene", "minutenlange"],
            ["estrés", "fallar", "minutos"]),
        ("Good Looking and Knows It", ["penalties", "Notoriety", "remember"],
            ["Abzüge", "Ruf", "wiedererkennbar"],
            ["penalizaciones", "Notoriedad", "recuerden"]),
        ("Groupthink", ["assisting", "sacrifice", "glitch"],
            ["Helfer", "opfern", "Helferpatzers"],
            ["ayudar", "sacrifica", "fallo"]),
        ("Mnemonic Vault", ["Palace", "yourself", "interrogation"],
            ["Gedächtnispalast", "selbst", "Verhören"],
            ["Palacio", "voluntariamente", "interrogatorios"]),
        ("Animal Familiar", ["one", "not its senses", "again"],
            ["ein", "nicht dessen Sinne", "erneut"],
            ["un", "no sus sentidos", "vuelve"]),
        ("Astral Bouncer", ["Assensing", "additional", "living"],
            ["Askennen", "zusätzliche", "Lebewesens"],
            ["auras", "adicionales", "vivos"]),
        ("Astral Infiltrator", ["Successful", "once", "alerts"],
            ["Erfolgreiches", "einmal", "alarmiert"],
            ["cruzarla", "una vez", "alerta"]),
        ("Rootkit", ["accuracy", "successful", "that turn"],
            ["erschwert", "Treffern", "dieser Runde"],
            ["dificulta", "acertar", "este turno"]),
        ("Big Baby", ["Physical", "overcome", "Stun"],
            ["Körperliche", "überwunden", "Betäubung"],
            ["Físico", "superar", "Aturdimiento"]),
        ("Well, Actually...", ["Disagreements", "distracting", "friends"],
            ["Meinungsverschiedenheiten", "ablenkende", "Freunden"],
            ["discrepancias", "distracción", "amigos"]),
        ("AIPS", ["spam zone", "Noise", "outside stressful"],
            ["Spamzonen", "Rauschen", "außerhalb"],
            ["spam", "Ruido", "sin estrés"]),
        ("Blank Slate", ["Without", "personasoft", "separate"],
            ["Ohne", "Personasoft", "gesonderter"],
            ["Sin", "personasoft", "aparte"]),
        ("Dead Emotion", ["one chosen", "gamemaster"],
            ["gewähltes", "Spielleitung"],
            ["una emoción", "dirección"]),
        ("Cynic", ["Others", "against you", "not improve"],
            ["Andere", "gegen dich", "nicht besser"],
            ["Otros", "contra ti", "no mejoran"]),
        ("Method Actor", ["Full days", "one role", "Willpower"],
            ["Volle Tage", "nur", "Willenskraft"],
            ["Días completos", "un papel", "Voluntad"]),
        ("Watch the Suit", ["Stun", "Etiquette", "Physical"],
            ["Betäubung", "Gebräuche", "körperliche"],
            ["Aturdimiento", "Etiqueta", "Físicas"]),
        ("Adept Healer", ["Empathic Healing", "more", "still transfers"],
            ["Empathische Heilung", "mehr", "weiterhin"],
            ["Empática", "más", "sigues"]),
        ("Apt Pupil", ["Magical", "time", "unchanged"],
            ["Magische", "kürzer", "unverändert"],
            ["mágico", "tiempo", "no cambia"]),
        ("Arcane Bodyguard", ["twice", "a third", "even alone"],
            ["doppelt", "Drittel", "auch allein"],
            ["duplica", "tercio", "incluso estando solo"]),
        ("AVRse", ["VR", "believe", "physical", "only"],
            ["VR", "körperlicher", "sicher erscheint"],
            ["RV", "creas", "solo", "física"]),
        ("Buddy System", ["Without teammates", "only reduces", "Hide"],
            ["Ohne Team", "nur", "Verbergen"],
            ["Sin equipo", "solo reduce", "Ocultarse"]),
        ("Discombobulated", ["AR or VR", "every test", "either"],
            ["AR oder VR", "alle Proben", "eine"],
            ["RA ni RV", "todas", "cualquiera"]),
        ("Biosonar", ["Ultrasonic pulses", "snapshots", "aiming and perception", "hearing improves", "sonic attacks hurt more"],
            ["Ultraschallimpulse", "Momentaufnahmen", "Zielen und Wahrnehmen", "Gehör wird besser", "Schallangriffe", "mehr Schaden"],
            ["pulsos ultrasónicos", "apuntar y percibir", "imágenes instantáneas", "mejora tu oído", "ataques sónicos", "más daño"]),
        ("Frog Tongue", ["light objects", "cannot operate tools", "requires Natural Venom", "successful tongue attack"],
            ["leichte Dinge", "keine Werkzeuge", "erfordert Natürliches Gift", "erfolgreichen Zungenangriff"],
            ["objetos ligeros", "no maneja herramientas", "requiere Veneno Natural", "acertar un ataque"]),
        ("Greasy Skin", ["Stress or exertion", "grapples", "mostly uncovered", "incompatible", "heavy body hair", "skin alterations"],
            ["Stress oder Anstrengung", "überwiegend freier Haut", "Haltegriffe", "unvereinbar", "Behaarung", "Hautveränderungen"],
            ["Estrés o esfuerzo", "agarres", "mayormente descubierta", "incompatible", "vello abundante", "alteraciones cutáneas"]),
        ("Data Anomaly", ["Running silent", "harder to spot", "sprites", "not hidden"],
            ["Schleichfahrt", "erschwert", "Sprites", "nicht verborgen"],
            ["modo silencioso", "cuesta más detectar", "sprites", "no estuviera oculto"]),
        ("Fade to Black", ["every mark on you", "attempt to hide", "same action", "partial removal does not"],
            ["alle Marken auf dir", "derselben Handlung", "Verbergen versuchen", "verbleibenden Marken"],
            ["todas las marcas sobre ti", "intentar Ocultarte", "misma acción", "no basta", "solo algunas"]),
        ("Ninja Vanish", ["Edge", "Free Action", "an opponent's marks on you", "shared copies", "other opponents' marks remain"],
            ["Edge", "Freien Handlung", "eines Gegners auf dir", "geteilten Kopien", "andere gegnerische Marken bleiben"],
            ["Edge", "acción gratuita", "un rival sobre ti", "copias compartidas", "las demás permanecen"]),
        ("Antipathy", ["opposed social tests", "harder", "not a penalty to every social action"],
            ["vergleichende Sozialproben", "erschweren", "nicht pauschal"],
            ["pruebas sociales enfrentadas", "dificulta", "no todas las acciones sociales"]),
        ("Lightweight", ["higher Addiction Rating", "risk", "without automatically"],
            ["höheren Suchtwert", "Suchtrisiko", "ohne", "automatisch"],
            ["mayor nivel de adicción", "riesgo", "no te vuelves adicto automáticamente"]),
        ("Lack of Focus", ["Repeated tests", "extended work", "over five minutes", "under a day", "escalating Composure", "failure forces breaks"],
            ["Wiederholte oder ausgedehnte Proben", "über fünf Minuten", "unter einem Tag", "zunehmend erschwerte Selbstbeherrschung", "Misslingen erzwingt Pausen"],
            ["repetidas o extendidas", "más de cinco minutos", "menos de un día", "Compostura progresivamente más difícil", "fallar fuerza pausas"]),
        ("Aware", ["Perceive astral", "no projection", "spellcasting, summoning, enchanting or adept powers"],
            ["Astralwahrnehmung", "weder Projektion", "Zauberei, Beschwörung, Verzauberung", "Adeptenkräfte"],
            ["percibir el espacio astral", "no proyectarse", "hechizos, invocar, encantar", "poderes de adepto"]),
        ("Explorer", ["perception and projection", "neither spells, spirits, enchanting nor adept powers"],
            ["Astralwahrnehmung und Astralprojektion", "weder Zauberei, Beschwörung, Verzauberung", "Adeptenkräfte"],
            ["percepción y proyección astrales", "no otorga hechizos, espíritus, encantamiento", "poderes de adepto"]),
        ("Enchanter", ["Create magical items", "perceive astral", "no spellcasting, summoning, astral projection or adept powers"],
            ["Verzauberung und Astralwahrnehmung", "weder Zauberei, Beschwörung, Astralprojektion", "Adeptenkräfte"],
            ["encantar objetos", "percibir el espacio astral", "no lanzar hechizos, invocar, proyectarse", "poderes de adepto"]),
        ("Solid Rep", ["reputation", "one chosen group", "neither universal", "every social test"],
            ["Ruf", "gewählten Gruppe", "kein allgemeiner Straßenruf", "jede soziale Probe"],
            ["reputación", "grupo elegido", "no concede", "todas las pruebas sociales"]),
        ("Legendary Rep", ["exceptional reputation", "one chosen group", "stronger than Solid Rep", "not universal"],
            ["gewählten Gruppe", "mehr Ansehen als Solid Rep", "keine weltweite", "kein allgemeiner"],
            ["más prestigio que Solid Rep", "grupo elegido", "no ofrece fama universal", "bono social general"]),
        ("Jack of All Trades Master of None", ["After creation", "Active and Knowledge", "less Karma", "higher ranks costs more"],
            ["Nach der Erschaffung", "Aktions- und Wissensfertigkeiten", "günstiger", "höhere Stufen werden teurer"],
            ["Después de la creación", "activas y de conocimiento", "menos Karma", "altos cuestan más"]),
        ("Day Job (10 hrs)", ["10 weekly work hours", "¥1,000 monthly", "fake rating four or higher", "absence risks job, pay and reputation"],
            ["10 Wochenstunden", "1.000 ¥ monatlich", "gefälscht mindestens Stufe vier", "Fehlen gefährdet Job, Gehalt und Ruf"],
            ["10 horas semanales", "1.000 ¥ mensuales", "falso de nivel cuatro o superior", "ausencias reiteradas", "empleo, salario y reputación"]),
        ("Day Job (20 hrs)", ["20 weekly work hours", "¥2,500 monthly", "fake rating four or higher", "absence risks job, pay and reputation"],
            ["20 Wochenstunden", "2.500 ¥ monatlich", "gefälscht mindestens Stufe vier", "Fehlen gefährdet Job, Gehalt und Ruf"],
            ["20 horas semanales", "2.500 ¥ mensuales", "falso de nivel cuatro o superior", "ausencias reiteradas", "empleo, salario y reputación"]),
        ("Day Job (40 hrs)", ["40 weekly work hours", "¥5,000 monthly", "fake rating four or higher", "absence risks job, pay and reputation"],
            ["40 Wochenstunden", "5.000 ¥ monatlich", "gefälscht mindestens Stufe vier", "Fehlen gefährdet Job, Gehalt und Ruf"],
            ["40 horas semanales", "5.000 ¥ mensuales", "falso de nivel cuatro o superior", "ausencias reiteradas", "empleo, salario y reputación"]),
        ("Scent Glands", ["smell aids tracking", "unmasked", "hinders social tests", "stress worsens both", "perfume only", "ordinary smell"],
            ["Aufspüren", "unverdeckt Sozialproben", "Stress verstärkt beides", "Parfüm", "nur den gewöhnlichen"],
            ["rastrearte", "pruebas Sociales si no se disimula", "estrés empeora ambos", "perfume solo", "olor habitual"]),
        ("Spirit Champion", ["Extra reagents", "beyond those setting limits", "improve Summoning", "Binding", "fewer reagents", "gains dice"],
            ["Zusätzliche Reagenzien", "neben denen fürs Limit", "verbessern Herbeirufen", "Binden", "weniger Reagenzien", "mehr Würfel"],
            ["adicionales", "destinados al límite", "mejoran Invocación", "Vinculación", "menos reactivos", "gana dados"]),
        ("Spirit Pariah", ["Summoning fails without", "extra reagents", "beyond those setting limits", "Binding", "more reagents", "loses dice"],
            ["Ohne zusätzliche Reagenzien", "scheitert Herbeirufen", "Limit-Reagenzien zählen dafür nicht", "Binden", "mehr Reagenzien", "weniger Würfeln"],
            ["Invocación falla sin", "reactivos extra", "aparte", "al límite", "Vinculación", "más reactivos", "pierde dados"]),
        ("Superhuman Psychosis", ["Better melee", "fewer suppression penalties", "worse Etiquette/Leadership", "harder retreat", "elite foes", "incompatible", "Code of Honor"],
            ["Besserer Nahkampf", "geringere Sperrfeuerabzüge", "schwächere Gebräuche/Führung", "erschwerter Rückzug", "Elitegegnern", "Ehrenkodex unvereinbar"],
            ["Mejora el combate cercano", "reduce penalizadores por supresión", "empeora Etiqueta/Liderazgo", "retiradas ante élites", "incompatible", "Código de Honor"]),
        ("Fame: Local", ["Local fame", "social skills", "Social limit", "one chosen sprawl", "Public Awareness", "anonymity harder"],
            ["Lokaler Ruhm", "soziale Fertigkeiten", "Soziale Limit", "gewählten Sprawl", "Prominenz", "erschwert Anonymität"],
            ["fama local", "habilidades sociales", "límite Social", "conurbación elegida", "notoriedad pública", "dificulta el anonimato"]),
        ("Fame: National", ["National fame", "social skills", "sufficient national-language", "Social limit", "Public Awareness", "anonymity harder"],
            ["Nationaler Ruhm", "soziale Fertigkeiten", "ausreichender Landessprache", "Soziale Limit", "Prominenz", "erschwert Anonymität"],
            ["fama nacional", "habilidades sociales", "suficiente dominio del idioma nacional", "límite Social", "notoriedad pública", "dificulta el anonimato"]),
        ("Fame: Megacorporate", ["Corporate fame", "social skills", "Social limit", "one chosen megacorporation", "Public Awareness", "anonymity harder"],
            ["Konzernruhm", "soziale Fertigkeiten", "Soziale Limit", "gewählten Megakonzerns", "Prominenz", "erschwert Anonymität"],
            ["fama corporativa", "habilidades sociales", "límite Social", "megacorporación elegida", "notoriedad pública", "dificulta el anonimato"]),
        ("Fame: Global", ["Worldwide fame", "strongly", "social skills", "Social limit", "Public Awareness", "anonymity much harder"],
            ["Weltweiter Ruhm", "soziale Fertigkeiten", "Soziales Limit deutlich", "hohe Prominenz", "schwerer, anonym"],
            ["fama mundial", "mejora mucho", "habilidades sociales", "límite Social", "alta notoriedad pública", "más difícil", "anonimato"]),
        ("Focused Concentration", ["one spell or complex form","up to this quality's rating","sustaining penalty","Drain and Fading still apply"],
            ["einen Zauber oder eine komplexe Form","bis zur Vorteilsstufe","Aufrechterhaltungsabzug","Entzug und Schwund bleiben"],
            ["un hechizo o forma compleja","hasta el nivel","mantenimiento","Drenaje y Desvanecimiento"]),
        ("Outdoorsman", ["Outdoors","natural environments","without stacking","urban Perception and Survival"],
            ["Naturproben","natürlichen Umgebungen","ohne Geländeboni zu stapeln","Wahrnehmung und Überleben in Städten"],
            ["grupo Supervivencia","entornos naturales","sin acumular","Percepción y Supervivencia en ciudades"]),
        ("Did You Just Call Me Dumb?", ["Social tests","critical glitches","even with hits","ordinary failed rolls","non-social"],
            ["sozialen Proben","mit Erfolgen kritisch","gewöhnlich misslungene Würfe","nichtsoziale"],
            ["pifias sociales","críticas","incluso con éxitos","fallos normales","no sociales"]),
        ("Simsense Vertigo", ["AR, VR and simsense","tests using those interfaces","smartlinks, simrigs and image links"],
            ["AR, VR und Simsinn","Proben bei ihrer Nutzung","Smartlinks, Simrigs und Bildverbindungen"],
            ["RA, RV y simsense","pruebas que los usan","smartlinks, simrigs y enlaces de imagen"]),
        ("Gifted Healer", ["one task","Stabilization, Diagnosis or Treatment","optional advanced","mundane or magical","Not repeatable"],
            ["eine Aufgabe","Stabilisierung, Diagnose oder Behandlung","optionalen erweiterten","weltlicher oder magischer","Nicht mehrfach"],
            ["una tarea","estabilización, diagnóstico o tratamiento","avanzadas opcionales","mundana o mágica","No repetible"]),
        ("Combat Junkie", ["Under stress","plans go wrong","start a fight","requires a test"],
            ["Stress","unerwarteten Planänderungen","Probe","Drang zur Gewalt"],
            ["estrés","imprevistos en el plan","iniciar una pelea","requiere una prueba"]),
        ("Poor Link", ["harder to seal","easier to resist","including beneficial","does not affect ordinary spells"],
            ["Erschwert das Versiegeln","erleichtert den Widerstand","hilfreichen Ritualen","gewöhnliche Zauber bleiben unverändert"],
            ["Dificulta sellar","facilita resistirlos","incluidos los beneficiosos","no afecta a hechizos ordinarios"]),
        ("Dealer Connection", ["one chosen vehicle class","once per class","Availability restrictions and price adjustments"],
            ["gewählten Fahrzeugklasse","einmal je Klasse","Verfügbarkeitsbeschränkungen","Preisanpassungen"],
            ["clase de vehículo elegida","una vez por clase","restricciones de Disponibilidad","ajustes de precio"]),
        ("Codeblock", ["one chosen","realistically used","requires a test","other Matrix actions are unaffected"],
            ["eine gewählte","mit Probe","tatsächlich nutzen würdest","andere Matrixhandlungen bleiben unverändert"],
            ["una acción matricial elegida","usarías realmente","requiere prueba","demás acciones de la Matriz no cambian"]),
        ("Astral Beacon", ["astral signatures","last longer","Assensing","requires Magic","without changing","dice pool"],
            ["astralen Signaturen","halten länger","askennen","Magie voraus","nicht den Würfelpool"],
            ["firmas astrales","duran más","Percepción Astral","requiere Magia","sin cambiar los dados"]),
        ("Metagenic Improvement (Body)", ["Body", "SURGE", "minimum and maximum", "by one"],
            ["Konstitution", "SURGE", "Minimum und Maximum", "um eins"],
            ["Constitución", "SURGE", "mínimo y máximo", "en uno"]),
        ("Metagenic Improvement (Agility)", ["Agility", "SURGE", "minimum and maximum", "by one"],
            ["Geschicklichkeit", "SURGE", "Minimum und Maximum", "um eins"],
            ["Agilidad", "SURGE", "mínimo y máximo", "en uno"]),
        ("Metagenic Improvement (Reaction)", ["Reaction", "SURGE", "minimum and maximum", "by one"],
            ["Reaktion", "SURGE", "Minimum und Maximum", "um eins"],
            ["Reacción", "SURGE", "mínimo y máximo", "en uno"]),
        ("Metagenic Improvement (Strength)", ["Strength", "SURGE", "minimum and maximum", "by one"],
            ["Stärke", "SURGE", "Minimum und Maximum", "um eins"],
            ["Fuerza", "SURGE", "mínimo y máximo", "en uno"]),
        ("Metagenic Improvement (Charisma)", ["Charisma", "SURGE", "minimum and maximum", "by one"],
            ["Charisma", "SURGE", "Minimum und Maximum", "um eins"],
            ["Carisma", "SURGE", "mínimo y máximo", "en uno"]),
        ("Metagenic Improvement (Intuition)", ["Intuition", "SURGE", "minimum and maximum", "by one"],
            ["Intuition", "SURGE", "Minimum und Maximum", "um eins"],
            ["Intuición", "SURGE", "mínimo y máximo", "en uno"]),
        ("Metagenic Improvement (Logic)", ["Logic", "SURGE", "minimum and maximum", "by one"],
            ["Logik", "SURGE", "Minimum und Maximum", "um eins"],
            ["Lógica", "SURGE", "mínimo y máximo", "en uno"]),
        ("Metagenic Improvement (Willpower)", ["Willpower", "SURGE", "minimum and maximum", "by one"],
            ["Willenskraft", "SURGE", "Minimum und Maximum", "um eins"],
            ["Voluntad", "SURGE", "mínimo y máximo", "en uno"]),
        ("Dermal Alteration (Bark Skin)", ["SURGE", "armor", "not Body or condition boxes", "incompatible"],
            ["SURGE", "Panzerung", "nicht Konstitution oder Zustandskästchen", "unvereinbar"],
            ["SURGE", "armadura", "no Constitución ni casillas", "Incompatible"]),
        ("Dermal Alteration (Granite Shell)", ["SURGE", "armor", "not Body or condition boxes", "incompatible"],
            ["SURGE", "Panzerung", "nicht Konstitution oder Zustandskästchen", "unvereinbar"],
            ["SURGE", "armadura", "no Constitución ni casillas", "Incompatible"]),
        ("Dermal Alteration (Rhino Hide)", ["SURGE", "armor", "not Body or condition boxes", "incompatible"],
            ["SURGE", "Panzerung", "nicht Konstitution oder Zustandskästchen", "unvereinbar"],
            ["SURGE", "armadura", "no Constitución ni casillas", "Incompatible"]),
        ("Dermal Alteration (Blubber)", ["SURGE", "against cold only", "not general armor", "incompatible"],
            ["SURGE", "nur Panzerung gegen Kälte", "nicht allgemeine", "unvereinbar"],
            ["SURGE", "solo contra frío", "no armadura general", "Incompatible"]),
        ("Dermal Alteration (Dragon Skin)", ["SURGE", "against fire only", "not general armor", "incompatible"],
            ["SURGE", "nur Panzerung gegen Feuer", "nicht allgemeine", "unvereinbar"],
            ["SURGE", "solo contra fuego", "no armadura general", "Incompatible"]),
        ("Functional Tail (Balance)", ["SURGE", "balancing", "Gymnastics", "Incompatible", "Vestigial Tail"],
            ["SURGE", "balancierender", "Akrobatik", "Unvereinbar", "verkümmerten"],
            ["SURGE", "para equilibrarte", "Gimnasia", "Incompatible", "Vestigial"]),
        ("Functional Tail (Paddle)", ["SURGE", "paddle-shaped", "Swimming", "Incompatible", "Vestigial Tail"],
            ["SURGE", "paddelförmiger", "Schwimm", "Unvereinbar", "verkümmerten"],
            ["SURGE", "en forma de remo", "Natación", "Incompatible", "Vestigial"]),
        ("Functional Tail (Prehensile)", ["SURGE", "grasping", "Gymnastics", "Incompatible", "Vestigial Tail"],
            ["SURGE", "greiffähiger", "Akrobatik", "Unvereinbar", "verkümmerten"],
            ["SURGE", "prensil", "Gimnasia", "Incompatible", "Vestigial"]),
        ("Trust Fund I", ["Medium", "assigned", "not free starting money", "not explained"],
            ["Mittelschicht", "zugewiesenen", "kein kostenloses Startgeld", "nicht erklärt"],
            ["Medio", "asignado", "no dinero inicial gratis", "no se explican"]),
        ("Trust Fund II", ["Low", "assigned", "not free starting money", "not explained"],
            ["Unterschicht", "zugewiesenen", "kein kostenloses Startgeld", "nicht erklärt"],
            ["Bajo", "asignado", "no dinero inicial gratis", "no se explican"]),
        ("Trust Fund III", ["High", "assigned", "not free starting money", "not explained"],
            ["Oberschicht", "zugewiesenen", "kein kostenloses Startgeld", "nicht erklärt"],
            ["Alto", "asignado", "no dinero inicial gratis", "no se explican"]),
        ("Trust Fund IV", ["Medium", "assigned", "not free starting money", "not explained"],
            ["Mittelschicht", "zugewiesenen", "kein kostenloses Startgeld", "nicht erklärt"],
            ["Medio", "asignado", "no dinero inicial gratis", "no se explican"]),
        ("Changeling (Class I SURGE)", ["I", "metagenic traits", "separate thirty-Karma", "not extra general Karma", "Only one"],
            ["I", "metagenische", "eigenem Dreißig-Karma", "kein zusätzliches", "Nur eine"],
            ["I", "metagénicos", "separado de treinta", "no Karma general", "Solo una"]),
        ("Changeling (Class II SURGE)", ["II", "metagenic traits", "separate thirty-Karma", "not extra general Karma", "Only one"],
            ["II", "metagenische", "eigenem Dreißig-Karma", "kein zusätzliches", "Nur eine"],
            ["II", "metagénicos", "separado de treinta", "no Karma general", "Solo una"]),
        ("Changeling (Class III SURGE)", ["III", "metagenic traits", "separate thirty-Karma", "not extra general Karma", "Only one"],
            ["III", "metagenische", "eigenem Dreißig-Karma", "kein zusätzliches", "Nur eine"],
            ["III", "metagénicos", "separado de treinta", "no Karma general", "Solo una"]),
        ("Arcane Arrester", ["SURGE", "each level", "two spell-resistance", "two levels", "Incompatible", "no spellcasting"],
            ["SURGE", "Jede Stufe", "zwei Zauberwiderstand", "zwei Stufen", "Unvereinbar", "kein Zauberbonus"],
            ["SURGE", "dos dados", "por nivel", "dos niveles", "Incompatible", "no mejora"]),
        ("Balance Receptor", ["Gymnastics", "without raising", "SURGE"],
            ["Akrobatik", "ohne", "SURGE"],
            ["Gimnasia", "sin aumentar", "SURGE"]),
        ("Beak", ["living costs", "swallowed toxins only", "SURGE"],
            ["Lebensstilkosten", "nur Widerstand gegen geschluckte Gifte", "SURGE"],
            ["costes de vida", "solo a toxinas ingeridas", "SURGE"]),
        ("Raptor Beak", ["SURGE", "living costs", "swallowed toxins", "beak weapon", "not yet explained"],
            ["SURGE", "Lebensstilkosten", "geschluckte Gifte", "Schnabelwaffe", "noch nicht erklärt"],
            ["SURGE", "costes de vida", "toxinas ingeridas", "arma de pico", "no está explicado"]),
        ("Ogre Stomach", ["living costs", "swallowed toxins only", "SURGE"],
            ["Lebensstilkosten", "nur Widerstand gegen geschluckte Gifte", "SURGE"],
            ["costes de vida", "solo a toxinas ingeridas", "SURGE"]),
        ("Dermal Deposits", ["armor", "not Body or condition boxes", "SURGE"],
            ["Panzerung", "nicht Konstitution oder Zustandskästchen", "SURGE"],
            ["armadura", "no Constitución ni casillas", "SURGE"]),
        ("Magnetoception", ["Navigation", "without raising", "SURGE"],
            ["Navigation", "ohne", "SURGE"],
            ["Navegación", "sin aumentar", "SURGE"]),
        ("Thorns", ["unarmed damage", "not attack dice", "penalizes Physical Active", "SURGE"],
            ["waffenlosen Schaden", "nicht Angriffswürfel", "erschwert", "körperliche", "SURGE"],
            ["daño sin armas", "no dados de ataque", "penaliza", "físicas activas", "SURGE"]),
        ("Vomeronasal Organ", ["smell-based Perception only", "other senses", "SURGE"],
            ["nur geruchsbasierte Wahrnehmung", "nicht andere Sinne", "SURGE"],
            ["solo Percepción", "olfato", "no otros sentidos", "SURGE"]),
        ("Webbed Digits", ["Swimming", "not skill rating or underwater breathing", "SURGE"],
            ["Schwimmproben", "nicht Fertigkeitswert oder Unterwasseratmung", "SURGE"],
            ["Natación", "no el nivel aprendido", "respirar bajo el agua", "SURGE"]),
        ("Will to Live", ["one overflow box per level", "Physical", "does not prevent unconsciousness or heal"],
            ["Überlaufbox je Stufe", "körperlichem", "weder Bewusstlosigkeit", "Verletzungen"],
            ["casilla de desbordamiento por nivel", "físico", "no evita inconsciencia ni cura"]),
        ("Infirm", ["Each level", "all natural physical-attribute maxima", "cannot exceed", "first level"],
            ["Jede Stufe", "körperlichen Attributmaxima", "ersten Stufe", "nicht überschreiten"],
            ["Cada nivel", "máximos físicos naturales", "primer nivel", "no pueden superar"]),
        ("Elevated Stress", ["Addiction", "toxin", "dependence type or exposure route", "only the relevant", "not every"],
            ["Sucht", "Gift", "Abhängigkeitstyp oder Aufnahmeweg", "nur der passende", "nicht alle"],
            ["adicción y toxinas", "dependencia o exposición", "solo la penalización", "sin acumular"]),
        ("Resistance to Pathogens/Toxins", ["Add two", "pathogens or toxins", "once", "never combine"],
            ["zwei Würfel", "Krankheitserreger oder Gifte", "einmal", "nicht addiert"],
            ["dos dados", "patógenos o toxinas", "una vez", "nunca acumules"]),
        ("Resistance to Pathogens and Toxins", ["Add one", "pathogens or toxins", "once", "never combine"],
            ["einen Würfel", "Krankheitserreger oder Gifte", "einmal", "nicht addiert"],
            ["un dado", "patógenos o toxinas", "una vez", "nunca acumules"]),
        ("Cyber-Singularity Seeker", ["pairs", "Willpower by one each", "up to two", "settings", "not unrelated"],
            ["paare", "Willenskraft um je eins", "höchstens zwei", "Einstellungen", "nicht beliebige"],
            ["par válido", "Voluntad en uno", "hasta dos", "configuración", "no otros"]),
        ("Dimmer Bulb", ["Each level", "Logic/Intuition", "defense", "Surprise", "memory", "intentions", "listed magical and addiction"],
            ["Jede Stufe", "Logik-/Intuition", "Abwehr", "Überraschung", "Gedächtnis", "Absichten", "Magie- und Sucht"],
            ["Cada nivel", "Lógica/Intuición", "defensa", "Sorpresa", "memoria", "intenciones", "mágicas y a adicción"]),
        ("Disgraced", ["GM", "criminals", "prejudice", "Etiquette"],
            ["Spielleitung", "Kriminelle", "Etikette", "Vorurteilen"],
            ["DJ", "criminales", "prejuicios", "Etiqueta"]),
        ("Elf Poser", ["Humans", "metatype or attributes", "hostility"],
            ["Menschen", "Metatyp oder Attribute", "Feindseligkeit"],
            ["humanos", "metatipo ni atributos", "hostilidad"]),
        ("Night Blindness", ["light/glare", "normal lighting", "buying off", "incompatible"],
            ["Blendungsabzüge", "normales Licht", "Nachteilsabbau", "unvereinbar"],
            ["deslumbramiento", "normal", "eliminar antes", "excluye"]),
        ("Juryrigger", ["temporary", "GM approval", "Gearhead", "burns out"],
            ["provisorische", "Spielleitung", "Gearhead", "zerstört"],
            ["provisionales", "DJ", "Gearhead", "quema"]),
        ("360-degree Eyesight", ["visual", "Surprise", "tests while moving", "distant shots", "remove", "prejudice"],
            ["Wahrnehmung", "Überraschung", "Proben in Bewegung", "Fernschüsse", "entfernen", "Vorurteile"],
            ["percepción", "Sorpresa", "pruebas en movimiento", "lejanos", "elimina", "prejuicios"]),
        ("Subtle Pilot", ["chosen Pilot", "directly controlled", "autonomous"],
            ["gewählten Pilotfertigkeit", "direkt gesteuerte", "autonome"],
            ["Pilotaje elegida", "directamente", "autónomos"]),
        ("Motion Sickness", ["acceleration", "speed", "passengers", "recovery takes time"],
            ["Beschleunigung", "Fahrzeugtempo", "Mitfahrenden", "Erholung Zeit"],
            ["aceleración", "velocidad", "pasajero", "lleva tiempo"]),
        ("Camouflage", ["slowly", "still and uncovered", "movement", "changed surroundings", "Excludes"],
            ["langsamer", "still und unbedeckt", "Bewegung", "Umgebungswechsel", "Keine weiteren"],
            ["lentamente", "inmóvil y descubierto", "moverte", "entorno", "Excluye"]),
        ("Radiation Sponge", ["less fatigue", "not slower deadly", "twice", "others", "Incompatible"],
            ["ermüdet weniger", "keine tödlichen", "doppelt", "andere", "Unvereinbar"],
            ["fatiga menos", "sin alargar", "doble", "otros", "Incompatible"]),
        ("Spirit Affinity", ["chosen spirit type", "extra service", "Binding", "outside", "no guaranteed", "Excludes"],
            ["gewählte Geisterart", "Zusatzdienst", "Binden", "außerhalb", "kein garantierter", "Keine Watcher"],
            ["tipo de espíritu", "servicio adicional", "Vinculación", "fuera", "no garantiza", "Excluye"]),
        ("Hobo with a Shotgun", ["Squatter", "temporarily", "all Mental", "full day", "Street"],
            ["Squatter", "vorübergehend", "alle geistigen", "ganzen Tag", "Straßenniveau"],
            ["Ocupa", "temporalmente", "todos los atributos mentales", "día entero", "Calle"]),
        ("Albinism II", ["glare", "sunlight", "Cybereyes", "prior Karma downgrade", "other Karma"],
            ["Blendung", "Sonne", "Cyberaugen", "mildere Stufe", "anderen Karmaausgaben"],
            ["deslumbramiento", "sol", "Ciberojos", "reducir primero", "otros gastos"]),
        ("Proboscis", ["awkwardly", "physical melee", "without extra attacks", "prejudice"],
            ["ungeschickt", "körperliche Nahkampfwaffe", "ohne zusätzliche Angriffe", "Vorurteile"],
            ["torpemente", "daño físico", "sin ataques extra", "prejuicios"]),
        ("Spirit Whisperer", ["resist", "one Force stronger", "on success", "declared Force"],
            ["widerstehen", "bei Erfolg", "eine Kraftstufe", "angesagte Kraftstufe"],
            ["resisten", "punto más de Fuerza", "si funciona", "Fuerza declarada"]),
        ("Scales", ["no armor", "identification or tracking", "prejudice", "Incompatible", "bioware"],
            ["Identifikation", "Suche", "nicht als Panzerung", "Vorurteile", "Unvereinbar", "Hautbioware"],
            ["identificarte o rastrearte", "sin dar armadura", "prejuicios", "Incompatibles", "cutáneo"]),
        ("Computer Illiterate", ["Computer", "electronics", "Matrix", "without lowering skill ratings", "double-counting", "Stress"],
            ["Computer", "Elektronik", "Matrix", "ohne Fertigkeitswerte", "doppelt", "Stress"],
            ["informáticas", "electrónicas", "Matriz", "sin bajar", "duplicar", "estrés"]),
        ("Glamour", ["Non-hostile", "social tests and limits", "except Intimidation tests", "Distinctive Style", "identify and track"],
            ["Nichtfeindselige", "Sozialproben und -limit", "außer Einschüchterungsproben", "Auffälliger Stil", "erkennbar und auffindbar"],
            ["no hostil", "pruebas y límite sociales", "excepto Intimidación", "Estilo Distintivo", "identificarte y rastrearte"]),
        ("Adrenaline Surge", ["opening Initiative Pass", "surprise", "compete"],
            ["ersten Initiativedurchgang", "Überraschung", "konkurrierende"],
            ["primera pasada", "sorpresa", "otros efectos"]),
        ("Signature", ["investigators", "track"], ["Ermittlern", "aufzuspüren"], ["investigadores", "rastrearte"]),
        ("Digital Doppelganger", ["chosen", "eligible fake SIN", "other identities"],
            ["gewählte", "geeignete gefälschte SIN", "andere Identitäten"],
            ["elegida", "falsa válida", "demás identidades"]),
        ("Perfect Time", ["rhythmic Performance", "Free Action", "not an extra attack"],
            ["rhythmische Darbietung", "Freie Handlung", "kein zusätzlicher Angriff"],
            ["Interpretación rítmica", "Acción Gratuita", "no otro ataque"]),
        ("Cold-Blooded", ["Cold", "coma", "thermal-only", "attacking"],
            ["Kälte", "Koma", "nur Wärmesicht", "angreifen"], ["frío", "coma", "solo mediante visión térmica", "atacarte"]),
        ("Speed Reading", ["general meaning", "details", "not gain perfect recall"],
            ["Überblick", "Details", "kein perfektes Gedächtnis"], ["sentido general", "detalles", "no obtienes memoria perfecta"]),
        ("Emotional Attachment", ["risk", "permanent loss", "temporarily", "buy off"],
            ["riskierst", "endgültiger Verlust", "vorübergehend", "Nachteil nicht abbaust"],
            ["Arriesgas", "perderlo definitivamente", "temporalmente", "Karma"]),
        ("Wanted", ["bounty", "hunters", "Karma buyoff"], ["Kopfgeld", "Jäger", "Karma"],
            ["recompensa", "cazadores", "Karma"]),
        ("Sensei", ["free teaching", "one chosen", "not free skill ranks"],
            ["unterrichtet kostenlos", "gewählte", "nicht geschenkt"],
            ["enseña gratis", "elegido", "no concede niveles"]),
        ("Driven", ["temporarily", "Willpower", "resisting", "endangers allies"],
            ["vorübergehend", "Willenskraft", "widerstehen", "Verbündete gefährdet"],
            ["temporalmente", "Voluntad", "resistirse", "peligro a tus aliados"]),
        ("Astral Hazing", ["including your own", "lingering", "GM agreement"],
            ["auch deine eigene", "längeres Verweilen", "Spielleitung"],
            ["incluida la propia", "permanecer", "Acuerda su alcance"]),
        ("Berserker", ["endangers allies", "boosts physical", "impairs mental", "implanted adrenaline pump"],
            ["Verbündete sind gefährdet", "körperliche Attribute steigen", "geistige sinken", "implantierte Adrenalinpumpe"],
            ["amenaza a aliados", "mejora atributos físicos", "empeora los mentales", "bomba de adrenalina implantada"]),
        ("Pacifist II", ["all violence", "mental performance", "lasting consequences", "Believing you killed"],
            ["jede Gewalt", "geistige Leistungen", "dauerhaft", "Glaubst du"],
            ["toda violencia", "rendimiento mental", "duradera", "Creer que has matado"]),
        ("Insomnia (Basic)", ["can slow Stun recovery", "delay Edge refresh", "normal recovery"],
            ["Betäubungsschaden verlangsamen", "Edge-Regeneration verzögern", "normale Erholung"],
            ["puede retrasar", "Aturdimiento y de Edge", "normalmente"]),
        ("Insomnia (Full)", ["Failed rest blocks Stun recovery", "delays Edge refresh", "normal recovery"],
            ["verhindert die Erholung von Betäubungsschaden", "Edge-Regeneration", "normale Erholung"],
            ["falla el descanso", "no recuperas Aturdimiento", "Edge se retrasa", "normalmente"]),
        ("Amnesia (Surface Loss)", ["retain practical abilities", "GM holds", "Knowledge skills", "cost Karma"],
            ["praktische Fähigkeiten erhalten", "Wissensfertigkeiten", "Spielleitung", "kostet Karma"],
            ["Conservas capacidades prácticas", "DJ guarda", "conocimiento", "cuesta Karma"]),
        ("Amnesia (Neural Deletion)", ["GM controls", "hidden character details", "story progress", "Karma buyoff"],
            ["Spielleitung", "verborgene Charakterdetails", "erzählerischen Fortschritt", "Abbau mit Karma"],
            ["DJ controla", "detalles ocultos", "progreso narrativo", "desventaja con Karma"]),
        ("Flashbacks I", ["roughly every other run", "temporary incapacitation", "unless resisted"],
            ["ungefähr bei jedem zweiten Run", "vorübergehend handlungsunfähig", "nicht widerstehst"],
            ["aproximadamente cada dos trabajos", "no los resistes", "incapacitan temporalmente"]),
        ("Flashbacks II", ["at least once per session", "temporary incapacitation", "unless resisted"],
            ["mindestens einmal je Spielsitzung", "vorübergehend handlungsunfähig", "nicht widerstehst"],
            ["al menos una vez por sesión", "no los resistes", "incapacitan temporalmente"]),
        ("Phobia (Uncommon, Mild)", ["Exposure", "rare trigger", "mildly", "all actions"],
            ["seltener Auslöser", "alle Handlungen leicht", "ausgesetzt"],
            ["Ante", "desencadenante raro", "levemente", "todas las acciones"]),
        ("Phobia (Common, Mild)", ["Exposure", "frequent trigger", "mildly", "all actions"],
            ["häufiger Auslöser", "alle Handlungen leicht", "ausgesetzt"],
            ["Ante", "desencadenante frecuente", "levemente", "todas las acciones"]),
        ("Phobia (Uncommon, Moderate)", ["rare trigger", "strongly", "all actions", "flee unless you resist"],
            ["seltener Auslöser", "alle Handlungen deutlich", "Selbstbeherrschung", "Fluchtimpuls"],
            ["desencadenante raro", "bastante", "todas las acciones", "resistir el miedo evita la huida"]),
        ("Phobia (Common, Moderate)", ["frequent trigger", "strongly", "all actions", "flee unless you resist"],
            ["häufiger Auslöser", "alle Handlungen deutlich", "Selbstbeherrschung", "Fluchtimpuls"],
            ["desencadenante frecuente", "bastante", "todas las acciones", "resistir el miedo evita la huida"]),
        ("Phobia (Uncommon, Severe)", ["rare trigger", "severely", "all actions", "failed resistance", "sustained flight"],
            ["seltener Auslöser", "alle Handlungen massiv", "scheitert", "anhaltender Flucht"],
            ["desencadenante raro", "gravemente", "todas las acciones", "no resistes", "huir durante un tiempo"]),
        ("Phobia (Common, Severe)", ["frequent trigger", "severely", "all actions", "failed resistance", "sustained flight"],
            ["häufiger Auslöser", "alle Handlungen massiv", "scheitert", "anhaltender Flucht"],
            ["desencadenante frecuente", "gravemente", "todas las acciones", "no resistes", "huir durante un tiempo"]),
        ("Poor Self Control (Braggart)", ["struggle to stop boasting", "prove exaggerated achievements"],
            ["Prahlerei schwer unterdrücken", "übertriebenen Erfolge beweisen"],
            ["Te cuesta dejar de presumir", "demostrar tus logros exagerados"]),
        ("Poor Self Control (Thrill Seeker)", ["riskiest choice", "briefly", "Initiative score", "not your Initiative dice"],
            ["größte Risiko", "kurz deinen Initiativewert", "nicht deine Initiativewürfel"],
            ["opción más peligrosa", "brevemente", "puntuación de Iniciativa", "no tus dados"]),
        ("Poor Self Control (Vindictive)", ["delaying retaliation", "does not remove", "harsher revenge"],
            ["aufgeschobene Vergeltung", "überzogener Rache nicht"],
            ["posponer la represalia", "no elimina", "venganza desmedida"]),
        ("Poor Self Control (Combat Monster)", ["retreat takes self-control", "opponents remain able", "losing"],
            ["Gegner noch kämpfen", "Rückzug schwer", "Niederlage"],
            ["Retirarte exige autocontrol", "rivales capaces de luchar", "perdiendo"]),
        ("The Beast's Way", ["Animal Handling","eligible","Choosing Mentor Spirit","quality budget"],
            ["Tierführung","begrenzte","Wahl eines Schutzgeists","Vorteilslimit"], ["Trato con Animales","limitados","Elegir Espíritu Mentor","límite de cualidades"]),
        ("The Spiritual Way", ["Conjuring","eligible","Choosing Mentor Spirit","quality budget"],
            ["Beschwören","begrenzte","Wahl eines Schutzgeists","Vorteilslimit"], ["Conjuración","limitados","Elegir Espíritu Mentor","límite de cualidades"]),
        ("The Magician's Way", ["individual","excluded","no power"],
            ["jeweiligen","ausgeschlossene","keine Kraft"], ["propio","excluidos","ninguno"]),
        ("Aged", ["per level","natural physical-attribute maxima","not a general dice penalty"],
            ["Jede Stufe","Attributmaxima","kein allgemeiner Würfelabzug"], ["Cada nivel","máximos naturales","no es una penalización general"]),
        ("Inherent Program", ["one eligible","that program","not yet explained here"],
            ["ein passendes","gewählten Programm","noch nicht erklärt"], ["un programa","programa elegido","aún no se explican"]),
        ("Mentor Spirit", ["Choose","benefits and demands","not yet explained here"],
            ["Wähle","Vorteilen und Anforderungen","noch nicht erklärt"], ["Elige","beneficios y exigencias","aún no se explica"]),
        ("Paragon", ["technomancer","benefits and drawbacks","not yet explained here"],
            ["Technomancer","Vor- und Nachteilen","noch nicht erklärt"], ["tecnomantes","ventajas y desventajas","aún no se explica"]),
        ("Crystalline Blade", ["Unarmed Combat","extra reach","not extra attack dice"],
            ["Waffenlosem Kampf","Reichweite","keine zusätzlichen Angriffswürfel"], ["combate sin armas","alcance adicional","no dados adicionales"]),
        ("Crystalline Shards", ["Throwing Weapons","positive armor modifier helps the target"],
            ["Wurfwaffen","stärkt die Zielpanzerung"], ["Armas Arrojadizas","refuerza la armadura"]),
        ("Spike Resistance", ["biofeedback","one extra die per level","does not increase Matrix armor"],
            ["Biofeedback","je Stufe","Matrixpanzerung steigt nicht"], ["biofeedback","por nivel","No aumenta la armadura"]),
        ("Low Pain Tolerance", ["Physical and Stun","sooner","without reducing"],
            ["Körperlicher und Betäubungsschaden","früher","ohne"], ["físico y de Aturdimiento","antes","sin reducir"]),
        ("Phenotypic Variation - Genetic Optimization (Body)", ["Body","natural maximum","cost points","Creation only","same attribute"],
            ["Konstitution","natürliche Maximum","keine Gratispunkte","Nur bei Erschaffung","dasselbe Attribut"], ["Constitución","máximo natural","sin puntos gratis","Solo al crear","ese atributo"]),
        ("Phenotypic Variation - Genetic Optimization (Agility)", ["Agility","natural maximum","cost points","Creation only","same attribute"],
            ["Geschicklichkeit","natürliche Maximum","keine Gratispunkte","Nur bei Erschaffung","dasselbe Attribut"], ["Agilidad","máximo natural","sin puntos gratis","Solo al crear","ese atributo"]),
        ("Phenotypic Variation - Genetic Optimization (Reaction)", ["Reaction","natural maximum","cost points","Creation only","same attribute"],
            ["Reaktion","natürliche Maximum","keine Gratispunkte","Nur bei Erschaffung","dasselbe Attribut"], ["Reacción","máximo natural","sin puntos gratis","Solo al crear","ese atributo"]),
        ("Phenotypic Variation - Genetic Optimization (Strength)", ["Strength","natural maximum","cost points","Creation only","same attribute"],
            ["Stärke","natürliche Maximum","keine Gratispunkte","Nur bei Erschaffung","dasselbe Attribut"], ["Fuerza","máximo natural","sin puntos gratis","Solo al crear","ese atributo"]),
        ("Phenotypic Variation - Genetic Optimization (Charisma)", ["Charisma","natural maximum","cost points","Creation only","same attribute"],
            ["Charisma","natürliche Maximum","keine Gratispunkte","Nur bei Erschaffung","dasselbe Attribut"], ["Carisma","máximo natural","sin puntos gratis","Solo al crear","ese atributo"]),
        ("Phenotypic Variation - Genetic Optimization (Intuition)", ["Intuition","natural maximum","cost points","Creation only","same attribute"],
            ["Intuition","natürliche Maximum","keine Gratispunkte","Nur bei Erschaffung","dasselbe Attribut"], ["Intuición","máximo natural","sin puntos gratis","Solo al crear","ese atributo"]),
        ("Phenotypic Variation - Genetic Optimization (Logic)", ["Logic","natural maximum","cost points","Creation only","same attribute"],
            ["Logik","natürliche Maximum","keine Gratispunkte","Nur bei Erschaffung","dasselbe Attribut"], ["Lógica","máximo natural","sin puntos gratis","Solo al crear","ese atributo"]),
        ("Phenotypic Variation - Genetic Optimization (Willpower)", ["Willpower","natural maximum","cost points","Creation only","same attribute"],
            ["Willenskraft","natürliche Maximum","keine Gratispunkte","Nur bei Erschaffung","dasselbe Attribut"], ["Voluntad","máximo natural","sin puntos gratis","Solo al crear","ese atributo"])
    ];

    private static readonly string[] FreeInsectSpiritSpecies =
    [
        "Cutter Ant", "Fire Ant", "Desert Locust", "Mole Cricket", "Subterranean Termite",
        "Hunter Wasp", "Bee", "Goliath Beetle", "Water Beetle", "House Centipede",
        "Tropical Centipede", "Century Cicada", "Trapdoor Spider", "Black Widow",
        "Burster Firefly", "Botfly", "Dragonfly", "Mimic Mantis", "Orchid Mantis",
        "Vampire Mosquito", "Tick", "Death's Head Moth", "Cryptid Moth", "Cave Roach", "Silverfish"
    ];

    private static void VerifyInsectSpiritSummaries(System.Xml.Linq.XElement[] catalog, string locale)
    {
        var qualities = catalog.Where(q => q.Element("name")!.Value.StartsWith("Free Insect Spirit: ", StringComparison.Ordinal)).ToArray();
        Require(qualities.Select(q => q.Element("name")!.Value["Free Insect Spirit: ".Length..]).Order()
            .SequenceEqual(FreeInsectSpiritSpecies.Order()), "Review every current insect-spirit variant, not only common powers.");
        var terms = locale switch
        {
            "de-AT" => new[] { "Auramaskierung", "normalen Waffen", "Leichte Insektizidallergie", "keine Beschwörung",
                "Feuer", "kälteempfindlich", "fliegen", "blind", "Schaden", "nur auf ihn selbst", "Gift",
                "Befehl der Königin", "festhalten", "Wände", "Sicht", "Sturzangriff", "Empathie", "Essenzverlust",
                "Essenz entziehen", "Krankheiten", "Furcht", "Zucker" },
            "es-MX" => new[] { "enmascaramiento", "armas normales", "Alergia leve a insecticidas", "sin Conjuración",
                "fuego", "frío", "volar", "ciego", "daño", "solo le afecta a él", "veneno",
                "orden de la reina", "inmovilizar", "paredes", "vista", "Ataque en Picado", "Empatía", "Pérdida de Esencia",
                "drenar Esencia", "enfermedades", "temor", "azúcar" },
            _ => new[] { "aura masking", "ordinary weapons", "Mild insecticide allergy", "no Conjuring",
                "fire", "cold", "flight", "blind", "damage", "only itself", "venom",
                "queen's command", "immobilize", "walls", "sight", "Dive Attack", "Empathy", "Essence Loss",
                "drain Essence", "disease", "dread", "sugar" }
        };
        foreach (var quality in qualities)
        {
            string name = quality.Element("name")!.Value;
            string summary = CreationFlowStrings.Get("Qualities.Summary." + Guid.Parse(quality.Element("id")!.Value).ToString("D"), "");
            var lines = CreationQualityInfo.Effects(quality.ToString());
            var bonus = quality.Element("bonus")!;
            bool Power(string power, string selection = "") => bonus.Element("critterpowers")!.Elements("power")
                .Any(p => p.Value == power && ((string?)p.Attribute("select") ?? "") == selection);
            bool Text(int index) => summary.Contains(terms[index], StringComparison.OrdinalIgnoreCase);
            Require(new[] { "Dual Natured", "Aura Masking", "Realistic Form" }.All(p => Power(p))
                && Power("Immunity", "Normal Weapons") && Power("Allergy", "Insecticides, Mild")
                && bonus.Element("skillgroupdisable")?.Value == "Conjuring"
                && bonus.Element("enableattribute")?.Element("name")?.Value == "MAG",
                "Shared insect-spirit help no longer matches the consumed capability and drawback scope: " + name);
            Require(summary.Length > 0 && lines[0] == summary && Enumerable.Range(0, 4).All(Text)
                && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                && lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Short spirit help must retain its allergy, unavailable skill group and unresolved external-power notice: " + name);
            foreach (var scope in new (int Index, bool Present)[] { (4, Power("Immunity", "Fire")),
                (5, Power("Vulnerability", "Cold")),
                (6, bonus.Elements("movementreplace").Any(m => m.Element("category")?.Value == "Fly")),
                (7, Power("Reduced Sense", "Blind")), (8, bonus.Element("damageresistance") is not null),
                (9, Power("Movement", "Self Only")), (10, Power("Venom")),
                (11, Power("Induced Dormancy", "Queen's Command")), (12, Power("Binding")),
                (13, Power("Wall Walking")), (14, Power("Innate Spell", "Mass Sight Removal")),
                (15, Power("Dive Attack")), (16, Power("Empathy")), (17, Power("Essence Loss")),
                (18, Power("Essence Drain")), (19, Power("Pestilence")),
                (20, Power("Innate Spell", "Foreboding")), (21, Power("Dietary Requirement", "Sugar")) })
                Require(Text(scope.Index) == scope.Present,
                    "A translated spirit summary lost or borrowed another variant's effect: " + name + " / " + terms[scope.Index]);
        }
    }

    private static void VerifyInfectedSummaries(System.Xml.Linq.XElement[] catalog, string locale)
    {
        var qualities = catalog.Where(q => q.Element("name")!.Value.StartsWith("Infected: ", StringComparison.Ordinal)).ToArray();
        Require(qualities.Length == 22, "Review each infected variant when the consumed catalog changes.");
        var terms = locale switch
        {
            "de-AT" => new[] { "Dual", "Alterung", "metahumanes Fleisch", "metahumanes Blut", "metahumane Knochen",
                "Salz", "Essenzentzug", "laufend", "Ruhezustand", "Infektion übertragen", "Adeptenfähigkeiten",
                "Zauberer", "Sonnenlicht", "Ungeziefer", "ätzendem Sekret", "Geruchssinn", "Eisenhut",
                "gegen Eisen verwundbar", "gegen Feuer verwundbar", "gegen Holz verwundbar", "gegen Silber und Holz verwundbar" },
            "es-MX" => new[] { "dual", "no envejece", "carne metahumana", "sangre metahumana", "huesos metahumanos",
                "sal,", "drena", "continua", "letargo", "transmitir la infección", "capacidades de adepto",
                "Hechicero", "luz solar", "alimañas", "secreciones corrosivas", "olfato", "acónito",
                "vulnerable al hierro", "vulnerable al fuego", "vulnerable a la madera", "vulnerable a plata y madera" },
            _ => new[] { "dual", "ageless", "metahuman flesh", "metahuman blood", "metahuman bone",
                "salt", "Essence drain", "ongoing Essence loss", "dormancy", "transmit the infection", "adept abilities",
                "spellcaster", "sunlight", "vermin", "corrosive secretions", "smell", "wolfsbane",
                "vulnerable to iron", "vulnerable to fire", "vulnerable to wood", "vulnerable to silver and wood" }
        };
        foreach (var quality in qualities)
        {
            string name = quality.Element("name")!.Value;
            string summary = CreationFlowStrings.Get("Qualities.Summary." + Guid.Parse(quality.Element("id")!.Value).ToString("D"), "");
            var bonus = quality.Element("bonus")!;
            bool Power(string power, string selection = "") => bonus.Element("critterpowers")!.Elements("power")
                .Any(p => p.Value == power && ((string?)p.Attribute("select") ?? "") == selection);
            bool Text(int index) => summary.Contains(terms[index], StringComparison.OrdinalIgnoreCase);
            // Plain-language alternatives for the same mandatory power, not optional powers.
            bool EssenceDrainText() => Text(6) || (locale == "de-AT" && summary.Contains("Essenz entziehen"))
                || (locale == "es-MX" && summary.Contains("drenaje de Esencia"))
                || (locale == "en-GB" && summary.Contains("Essence-draining"));
            var lines = CreationQualityInfo.Effects(quality.ToString());
            Require(summary.Length > 0 && summary.Length <= 240 && lines[0] == summary
                && lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))
                && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                "Brief infected help must stay source-bound and retain the incomplete external-power notice: " + name);
            foreach (var scope in new (int Index, bool Present)[] { (0, Power("Dual Natured")),
                (1, Power("Immunity", "Age")), (2, Power("Dietary Requirement", "Metahuman Flesh")),
                (3, Power("Dietary Requirement", "Metahuman Blood")), (4, Power("Dietary Requirement", "Metahuman Bone")),
                (5, Power("Dietary Requirement", "Salt")), (7, Power("Essence Loss")),
                (8, Power("Induced Dormancy", "Lack of Air (Essence) Minutes")), (9, Power("Infection")),
                (10, bonus.Element("unlockskills")?.Value == "Adept"), (11, bonus.Element("unlockskills")?.Value == "Magician"),
                (12, bonus.Element("critterpowers")!.Elements("power").Any(p => p.Value == "Allergy"
                    && ((string?)p.Attribute("select") ?? "").StartsWith("Sunlight, ", StringComparison.Ordinal))),
                (13, Power("Animal Control", "Vermin")), (14, Power("Secretion/Substance Extrusion", "Corrosive")),
                (15, Power("Enhanced Senses", "Smell") && name.Contains("Ghoul")),
                (16, Power("Allergy", "Aconite a.k.a. Wolf's Bane, Moderate")),
                (17, Power("Vulnerability", "Iron")), (18, Power("Vulnerability", "Fire")),
                (19, Power("Vulnerability", "Wood") && !Power("Vulnerability", "Silver")),
                (20, Power("Vulnerability", "Silver") && Power("Vulnerability", "Wood")) })
                Require(Text(scope.Index) == scope.Present,
                    "An infected summary lost or borrowed another variant's benefit or drawback: " + name + " / " + terms[scope.Index]);
            Require(EssenceDrainText() == Power("Essence Drain"),
                "Essence drain must not be borrowed by variants that only pay a fixed Essence cost: " + name);
            // A source ID alone must never admit prose after a custom-data change.
            var changed = new System.Xml.Linq.XElement(quality);
            changed.Element("bonus")!.Element("critterpowers")!.Add(
                new System.Xml.Linq.XElement("power", "Regeneration"));
            var changedLines = CreationQualityInfo.Effects(changed.ToString());
            Require(!changedLines.Contains(summary)
                && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                "Infected help must reject changed source powers: " + name);
        }
    }

    private static void VerifyOptionalPowerAndDrakeSummaries(System.Xml.Linq.XElement[] catalog,
        System.Xml.Linq.XElement[] powers, string locale)
    {
        int language = locale == "de-AT" ? 1 : locale == "es-MX" ? 2 : 0;
        string eligible = new[] { "only to eligible Infected", "nur für passende Infiziertenformen",
            "solo para formas de infectado compatibles" }[language];
        var cases = new (string Suffix, string Power, string Selection, string[] Scope)[]
        {
            ("Armor", "Armor", "", ["natural armor", "natürliche Panzerung", "armadura natural"]),
            ("Compulsion", "Compulsion", "", ["perform an action", "Handlung zu zwingen", "obligar"]),
            ("Enhanced Sense (Hearing)", "Enhanced Senses", "Hearing", ["hearing", "Gehör", "oído"]),
            ("Enhanced Sense (Low-Light Vision)", "Enhanced Senses", "Low-Light Vision", ["dim light", "schwachem Licht", "poca luz"]),
            ("Enhanced Sense (Smell)", "Enhanced Senses", "Smell", ["smell", "Geruchssinn", "olfato"]),
            ("Enhanced Sense (Taste)", "Enhanced Senses", "Taste", ["taste", "Geschmackssinn", "gusto"]),
            ("Enhanced Sense (Thermographic Vision)", "Enhanced Senses", "Thermographic Vision", ["heat differences", "Wärmeunterschiede", "temperatura"]),
            ("Enhanced Sense (Visual Acuity)", "Enhanced Senses", "Visual Acuity", ["visual details", "optischer Details", "detalles visuales"]),
            ("Fear", "Fear", "", ["fear", "Furcht", "miedo"]),
            ("Immunity (Fire)", "Immunity", "Fire", ["against fire", "gegen Feuer", "contra el fuego"]),
            ("Immunity (Pathogens)", "Immunity", "Pathogens", ["disease-causing", "Krankheitserreger", "patógenos"]),
            ("Immunity (Toxins)", "Immunity", "Toxins", ["toxins", "Toxine", "toxinas"]),
            ("Influence", "Influence", "", ["suggestion", "Gedanken einzugeben", "sugerencia"]),
            ("Magical Guard", "Magical Guard", "", ["Counterspelling", "Antimagie", "Contraconjuros"]),
            ("Mist Form", "Mist Form", "", ["body into mist", "Körper in Nebel", "cuerpo en niebla"]),
            ("Paralyzing Howl", "Paralyzing Howl", "", ["howl can paralyze", "Heulen", "aullido puede paralizar"]),
            ("Regeneration", "Regeneration", "", ["implant grades", "Implantatgrade", "grados de implantes"])
        };
        Require(catalog.Count(q => q.Element("name")!.Value.StartsWith("Infected Optional Power: ", StringComparison.Ordinal)) == cases.Length,
            "Review any newly added optional power instead of silently leaving its help behind.");
        void VerifyBoundHelp(System.Xml.Linq.XElement quality, string summary)
        {
            var lines = CreationQualityInfo.Effects(quality.ToString());
            Require(summary.Length > 0 && summary.Length <= 240 && lines[0] == summary
                && lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))
                && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                "Brief power help must precede effects without hiding unresolved external rules.");
            var changed = new System.Xml.Linq.XElement(quality);
            changed.SetElementValue("karma", "999");
            var changedLines = CreationQualityInfo.Effects(changed.ToString());
            Require(!changedLines.Contains(summary)
                && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                "Power help must reject a changed definition, even with the same ID.");
        }
        foreach (var rule in cases)
        {
            var quality = catalog.Single(q => q.Element("name")!.Value == "Infected Optional Power: " + rule.Suffix);
            var power = quality.Element("bonus")!.Element("critterpowers")!.Elements("power").Single();
            Require(power.Value == rule.Power && ((string?)power.Attribute("select") ?? "") == rule.Selection,
                "Optional power help no longer describes the source-selected capability: " + rule.Suffix);
            var required = quality.Element("required")!.Element("oneof")!.Elements("quality").ToArray();
            Require(required.Length > 0 && required.All(q => q.Value.StartsWith("Infected: ", StringComparison.Ordinal)),
                "Optional powers must retain their form-specific admission.");
            string summary = CreationFlowStrings.Get("Qualities.Summary." + Guid.Parse(quality.Element("id")!.Value).ToString("D"), "");
            Require(summary.Contains(eligible, StringComparison.OrdinalIgnoreCase)
                && summary.Contains(rule.Scope[language], StringComparison.OrdinalIgnoreCase),
                "Localized optional-power help lost its effect or eligibility restriction: " + rule.Suffix);
            VerifyBoundHelp(quality, summary);
        }
        // Check the external capability facts used by these two short descriptions.
        var guard = powers.Single(p => p.Element("id")!.Value == "f5a654a0-91e3-4555-aa22-792d012348b8");
        Require(guard.Element("bonus")!.Element("unlockskills")?.Value == "Name"
            && (string?)guard.Element("bonus")!.Element("unlockskills")!.Attribute("name") == "Counterspelling",
            "Magical Guard help must agree with its actual skill unlock.");
        var regeneration = powers.Single(p => p.Element("id")!.Value == "244ccf0f-fc77-4690-9441-1cfeb7a6dc2a");
        Require(new[] { "disablecyberwaregrade", "disablebiowaregrade" }.All(tag =>
            regeneration.Element("bonus")!.Elements(tag).Any(e => e.Value == "Standard")),
            "Regeneration help must retain the real implant-grade restriction.");

        var drakes = catalog.Where(q => q.Element("name")!.Value.StartsWith("Dracoform (", StringComparison.Ordinal)).ToArray();
        Require(drakes.Length == 4, "Review new Drake forms before asserting complete form help.");
        var words = language switch
        {
            1 => new[] { "dual", "Gesucht", "Panzerung", "Feuer", "Flügel", "Wasseranpassung",
                "Konstitution", "Stärke", "Geschicklichkeit", "Logik", "Charisma", "Reaktion", "Willenskraft", "Intuition",
                "Krallen", "Hörner", "Schwanz", "Fangzähnen" },
            2 => new[] { "dual", "Buscado", "armadura", "fuego", "alas", "adaptación acuática",
                "Cuerpo", "Fuerza", "Agilidad", "Lógica", "Carisma", "Reacción", "Voluntad", "Intuición",
                "garras", "cuernos", "cola", "colmillos" },
            _ => new[] { "dual", "Wanted", "armor", "fire", "wings", "aquatic adaptation",
                "Body", "Strength", "Agility", "Logic", "Charisma", "Reaction", "Willpower", "Intuition",
                "claws", "horns", "tail", "fangs" }
        };
        foreach (var quality in drakes)
        {
            var bonus = quality.Element("bonus")!;
            var names = bonus.Element("critterpowers")!.Elements("power").Select(p => p.Value).ToArray();
            Require(new[] { "Dual Natured", "Shift (Dracoform)", "Hardened Armor", "Hardened Mystic Armor" }.All(names.Contains)
                && bonus.Element("critterpowers")!.Elements("power").Any(p => p.Value == "Elemental Attack" && (string?)p.Attribute("select") == "Fire")
                && bonus.Element("addqualities")!.Element("addquality")?.Value == "Wanted",
                "Drake help must preserve its actual form capabilities and granted drawback.");
            string summary = CreationFlowStrings.Get("Qualities.Summary." + Guid.Parse(quality.Element("id")!.Value).ToString("D"), "");
            bool Text(int index) => summary.Contains(words[index], StringComparison.OrdinalIgnoreCase);
            Require(Enumerable.Range(0, 4).All(Text) && Text(4) == names.Contains("Vestigial Wings")
                && Text(5) == names.Contains("Underwater Adaptation"),
                "Drake help lost its Wanted drawback or borrowed another form's adaptation.");
            foreach (var (attribute, index) in new[] { ("BOD", 6), ("STR", 7), ("AGI", 8), ("LOG", 9),
                ("CHA", 10), ("REA", 11), ("WIL", 12), ("INT", 13) })
                Require(Text(index) == bonus.Elements("specificattribute").Any(a => a.Element("name")?.Value == attribute),
                    "Drake attribute summary differs from its exact form: " + quality.Element("name")!.Value);
            var weapon = quality.Element("naturalweapons")!.Element("naturalweapon")!.Element("name")!.Value;
            foreach (var (name, index) in new[] { ("Dracoform Claws", 14), ("Dracoform Horns", 15),
                ("Dracoform Tail", 16), ("Dracoform Fangs", 17) })
                Require(Text(index) == (weapon == name), "Drake help borrowed another form's natural attack.");
            VerifyBoundHelp(quality, summary);
        }
    }

    private static void VerifyTradeoffQualitySummaries(System.Xml.Linq.XElement[] catalog, string locale)
    {
        // Original brief copy: Data Trails p46, Forbidden Arcana p182 and
        // No Future p177. No tabletop procedures or edition-specific bounty tables.
        var cases = new (string Name, string[] English, string[] German, string[] Spanish)[]
        {
            ("Code of Honor: Like a Boss", ["Deliberate direct Matrix damage", "costs Karma", "data-bomb traps", "exempt"],
                ["Absichtlicher direkter Matrixschaden", "kostet Karma", "Datenbombenfallen", "ausgenommen"],
                ["daño directo intencional", "cuesta Karma", "bombas de datos", "exentas"]),
            ("Mentor's Mask", ["Drain or", "adept power", "more noticeable", "cannot switch"],
                ["Entzug oder", "Adeptenkraft", "leichter bemerkt", "nicht abschalten"],
                ["Drenaje o", "poder de adepto", "más perceptible", "no puede desactivarse"]),
            ("Stolen Gear", ["starting equipment", "theft", "bounty", "hunters"],
                ["beginnst", "gestohlener Ausrüstung", "Kopfgeld", "Jäger"],
                ["Empiezas", "equipo robado", "recompensa", "cazadores"])
        };
        foreach (var rule in cases)
        {
            var quality = catalog.Single(q => q.Element("name")!.Value == rule.Name);
            string summary = CreationFlowStrings.Get("Qualities.Summary."
                + Guid.Parse(quality.Element("id")!.Value).ToString("D"), "");
            string[] scope = locale == "de-AT" ? rule.German : locale == "es-MX" ? rule.Spanish : rule.English;
            Require(summary.Length > 0 && summary.Length <= 160
                && summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 25
                && !Regex.IsMatch(summary, @"\d")
                && scope.All(term => summary.Contains(term, StringComparison.OrdinalIgnoreCase))
                && CreationQualityInfo.Effects(quality.ToString())[0] == summary,
                "Trade-off help must preserve its restriction in concise localized copy: " + rule.Name);
            var changed = new System.Xml.Linq.XElement(quality);
            changed.SetElementValue("karma", "999");
            var changedLines = CreationQualityInfo.Effects(changed.ToString());
            Require(!changedLines.Contains(summary)
                && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                "Trade-off help must reject amended definitions with the same ID: " + rule.Name);
        }
        var code = catalog.Single(q => q.Element("name")!.Value == "Code of Honor: Like a Boss");
        Require(code.Element("karma")?.Value == "-15"
            && code.Element("required")!.Element("oneof")!.Element("quality")?.Value == "Technomancer"
            && code.Element("required")!.Element("oneof")!.Element("skill")!.Element("name")?.Value == "Hacking"
            && code.Element("required")!.Element("oneof")!.Element("skill")!.Element("val")?.Value == "3",
            "Code-of-Honor help must retain its original eligibility.");
        var mask = catalog.Single(q => q.Element("name")!.Value == "Mentor's Mask");
        Require(mask.Element("bonus")!.Element("drainvalue")?.Value == "-1"
            && mask.Element("bonus")!.Element("adeptpowerpoints")?.Value == "1"
            && mask.Element("required")!.Element("oneof")!.Element("quality")?.Value == "Mentor Spirit",
            "Mentor's Mask help must remain bound to its mentor and source benefits.");
        var gear = catalog.Single(q => q.Element("name")!.Value == "Stolen Gear");
        Require(gear.Element("contributetobp")?.Value == "False"
            && gear.Element("bonus")!.Element("nuyenamt")?.Value == "10000"
            && (string?)gear.Element("bonus")!.Element("nuyenamt")!.Attribute("condition") == "Stolen",
            "Stolen Gear must not be described as free Karma or unrestricted income.");
    }

    private static void VerifyCompulsionQualitySummaries(System.Xml.Linq.XElement[] catalog, string locale)
    {
        // Run Faster p158: summarize the chosen scope and resistance, not its
        // examples or a tabletop procedure. Roman grades are distinct catalog
        // definitions, not purchased levels of one definition.
        string[] grades = ["I", "II", "III", "IV"];
        string[] scopes = ["Personal", "Public Single Aspect", "Public Broad Aspect"];
        string[] degreeWords = locale == "de-AT" ? ["leichter", "mäßiger", "starker", "sehr starker"]
            : locale == "es-MX" ? ["leve", "moderada", "fuerte", "muy fuerte"]
            : ["mild", "moderate", "strong", "very strong"];
        string[] scopeWords = locale == "de-AT"
            ? ["dein persönliches Umfeld", "einen einzelnen Aspekt deines öffentlichen Umfelds", "weite Bereiche deines öffentlichen Umfelds"]
            : locale == "es-MX" ? ["tu entorno privado", "un aspecto concreto de tu entorno público", "aspectos amplios de tu entorno público"]
            : ["your private surroundings", "one aspect of your public surroundings", "broad aspects of your public surroundings"];
        string resistance = locale == "de-AT" ? "Selbstbeherrschungsprobe" : locale == "es-MX" ? "prueba de Compostura" : "Composure test";
        var summaries = new HashSet<string>(StringComparer.Ordinal);
        Require(catalog.Count(q => q.Element("name")!.Value.StartsWith("Poor Self Control (Compulsive ", StringComparison.Ordinal)) == 12,
            "The compulsion family must include every scope and grade, not an assumed subset.");
        for (int scope = 0; scope < scopes.Length; scope++)
        for (int grade = 0; grade < grades.Length; grade++)
        {
            string name = $"Poor Self Control (Compulsive {grades[grade]}, {scopes[scope]})";
            var quality = catalog.Single(q => q.Element("name")!.Value == name);
            Require(quality.Element("karma")!.Value == (-(2 * (grade + 1) + scope + 2)).ToString(CultureInfo.InvariantCulture)
                && quality.Element("category")?.Value == "Negative"
                && quality.Element("bonus")!.Element("selecttext") is not null,
                "Compulsion help must retain its scope/grade cost and user-chosen context: " + name);
            string summary = CreationFlowStrings.Get("Qualities.Summary." + Guid.Parse(quality.Element("id")!.Value).ToString("D"), "");
            var effects = CreationQualityInfo.Effects(quality.ToString());
            Require(summary.Length > 0 && summary.Length <= 160
                && summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 25
                && !Regex.IsMatch(summary, @"\d") && summaries.Add(summary)
                && summary.Contains(degreeWords[grade], StringComparison.Ordinal)
                && summary.Contains(scopeWords[scope], StringComparison.Ordinal)
                && summary.Contains(resistance, StringComparison.Ordinal)
                && effects[0] == summary
                && !effects.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                "Every compulsion variant needs distinct short localized help, not a missing-description notice: " + name);
            foreach (string field in new[] { "name", "karma" })
            {
                var changed = new System.Xml.Linq.XElement(quality);
                changed.SetElementValue(field, field == "name" ? "Different compulsion scope" : "-999");
                var changedEffects = CreationQualityInfo.Effects(changed.ToString());
                Require(!changedEffects.Contains(summary)
                    && changedEffects.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                    "A retained ID must not transfer compulsion copy to a changed scope or cost.");
            }
        }
    }

    private static void VerifyNaturalVenomQualitySummaries(System.Xml.Linq.XElement[] catalog, string locale)
    {
        // Run Faster p117. Summaries explain delivery and possible effects,
        // not dose/resistance procedures. Immunity applies to one's own venom.
        string[] vectors = ["Exhaled", "Spat", "Injected"];
        string[] severities = ["Mild", "Moderate", "Serious", "Deadly"];
        string[] words = locale == "de-AT"
            ? ["Ausgeatmetes", "Gespucktes Kontaktgift", "Fangzähnen", "Betäubungsschaden", "körperlichen Schaden", "Desorientierung", "Übelkeit", "Lähmung", "gegen dein eigenes Gift bist du immun"]
            : locale == "es-MX"
                ? ["exhalado", "contacto escupido", "Necesitas colmillos", "Aturdimiento", "Físico", "desorientación", "náuseas", "parálisis", "inmune a tu propio veneno"]
                : ["Exhaled", "Spat contact", "Fangs let you inject", "Stun damage", "Physical damage", "disorientation", "nausea", "paralysis", "immune to your own venom"];
        var summaries = new HashSet<string>(StringComparer.Ordinal);
        Require(catalog.Count(q => q.Element("name")!.Value.StartsWith("Natural Venom (", StringComparison.Ordinal)) == 12,
            "All Natural Venom delivery/severity variants need help.");
        for (int vector = 0; vector < vectors.Length; vector++)
        for (int severity = 0; severity < severities.Length; severity++)
        {
            string name = $"Natural Venom ({vectors[vector]}, {severities[severity]})";
            var quality = catalog.Single(q => q.Element("name")!.Value == name);
            bool needsFangs = quality.Element("required")!.Element("allof")?.Elements("quality").Any(q => q.Value == "Fangs") == true;
            Require(needsFangs == (vector == 2)
                && quality.Element("required")!.Element("oneof")!.Elements("quality").Count() == 3
                && quality.Element("forbidden")!.Element("oneof")!.Elements("quality").Any(q => q.Value == "Corrosive Spit")
                && quality.Element("metagenic")?.Value == "True",
                "Venom copy must remain bound to actual Fangs, changeling and incompatibility restrictions: " + name);
            string summary = CreationFlowStrings.Get("Qualities.Summary." + Guid.Parse(quality.Element("id")!.Value).ToString("D"), "");
            bool Has(int index) => summary.Contains(words[index], StringComparison.OrdinalIgnoreCase);
            var effects = CreationQualityInfo.Effects(quality.ToString());
            Require(summary.Length > 0 && summary.Length <= 180
                && summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 25
                && !Regex.IsMatch(summary, @"\d") && summaries.Add(summary)
                && Enumerable.Range(0, 3).All(i => Has(i) == (i == vector))
                && Has(3) == (severity < 2) && Has(4) == (severity >= 2)
                && Has(5) == (severity != 3) && Has(6) == (severity == 1 || severity == 3)
                && Has(7) == (severity >= 2) && Has(8)
                && effects[0] == summary
                && !effects.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                "Venom summary mixed delivery, symptoms, damage kind or immunity scope: " + name);
            var changed = new System.Xml.Linq.XElement(quality);
            changed.Element("required")!.Remove();
            var changedEffects = CreationQualityInfo.Effects(changed.ToString());
            Require(!changedEffects.Contains(summary)
                && changedEffects.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                "Removing a prerequisite must reject the old venom description even with the same ID.");
        }
    }

    private static void VerifyQualitySummaryContent(string contentRoot)
    {
        var catalog = System.Xml.Linq.XDocument.Load(Path.Combine(contentRoot, "data", "qualities.xml"))
            .Root!.Element("qualities")!.Elements("quality").ToArray();
        var critterPowerDefinitions = System.Xml.Linq.XDocument.Load(Path.Combine(contentRoot, "data", "critterpowers.xml"))
            .Root!.Element("powers")!.Elements("power").ToArray();
        static string SummaryKey(System.Xml.Linq.XElement quality)
            => "Qualities.Summary." + Guid.Parse(quality.Element("id")!.Value).ToString("D");
        var oldCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                int authored = 0, missing = 0, partial = 0;
                foreach (var quality in catalog)
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    if (summary.Length > 0)
                    {
                        authored++;
                        Require(summary.Length <= 320 && summary.Split((char[]?)null,
                                StringSplitOptions.RemoveEmptyEntries).Length <= 50
                            && !summary.Contains('\n') && !summary.Contains('\r'),
                            "Quality help must remain a short summary, not a full tabletop procedure.");
                    }
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(lines.Count > 0 && lines.All(line => !string.IsNullOrWhiteSpace(line)),
                        "Every catalog entry needs an explanation or an honest missing-description state.");
                    string text = string.Join(" ", lines);
                    Require(!Regex.IsMatch(text, @"[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}", RegexOptions.IgnoreCase)
                        && !text.Contains("rulebook", StringComparison.OrdinalIgnoreCase)
                        && !text.Contains("Regelbuch", StringComparison.OrdinalIgnoreCase)
                        && !text.Contains("consulta el manual", StringComparison.OrdinalIgnoreCase),
                        "Quality information exposed a machine identity or deferred to a book.");
                    if (lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))) missing++;
                    if (lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))) partial++;
                    if (summary.Length > 0) Require(lines[0] == summary,
                        "The original summary must precede technical effects: " + quality.Element("name")!.Value);
                }
                Require(authored >= 803 && authored == catalog.Length && missing == 0,
                    "Every real catalog quality needs its own localized, source-bound explanation.");
                VerifyTradeoffQualitySummaries(catalog, locale);
                VerifyCompulsionQualitySummaries(catalog, locale);
                VerifyNaturalVenomQualitySummaries(catalog, locale);
                foreach (var rule in ConciseQualitySummaries)
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == rule.Name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    string[] scope = locale == "de-AT" ? rule.German : locale == "es-MX" ? rule.Spanish : rule.English;
                    Require(summary.Length > 0 && summary.Length <= 180
                        && summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 25
                        // Hours and income distinguish the three jobs; short help may retain those facts.
                        && (rule.Name is "Day Job (10 hrs)" or "Day Job (20 hrs)" or "Day Job (40 hrs)"
                            || !Regex.IsMatch(summary, @"\d"))
                        && scope.All(term => summary.Contains(term, StringComparison.OrdinalIgnoreCase))
                        && CreationQualityInfo.Effects(quality.ToString())[0] == summary,
                        "Brief help must retain the benefit and important drawback without a numeric procedure: " + rule.Name);
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.SetElementValue("karma", "999");
                    var changedLines = CreationQualityInfo.Effects(changed.ToString());
                    Require(!changedLines.Contains(summary)
                        && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                        "Shortening copy must not detach it from its source definition: " + rule.Name);
                }
                foreach (string name in new[] { "Adept Healer", "Apt Pupil", "Arcane Bodyguard",
                    "Animal Familiar", "Astral Bouncer", "Astral Infiltrator", "Mnemonic Vault",
                    "Alchemical Bomb Maker", "Arcane Improviser", "Archivist",
                    "Dual-Natured Defender", "Durable Preparations", "Elemental Master",
                    "Flesh Sculpter", "Healer", "Illusionist",
                    "Items of Power", "Mage Hunter I", "Mage Hunter II", "Mage Hunter III",
                    "Brilliant Heuristics", "Groveler", "Hold the Door",
                    "Fractal Punch", "Lone Wolf", "Team Player", "Natural Hacker",
                    "One With the Matrix I", "One With the Matrix II", "One With the Matrix III",
                    "Missile Deflector", "Mystic Foreman", "Mystic Pitcher",
                    "Pacifist Adept", "Potion Maker", "Practiced Alchemist",
                    "Sprite Affinity", "Trust Data, Not Lore", "Trust Lore, Not Data",
                    "Puppet Master", "Reckless Spell Master", "Renaissance Ritualist", "Shock Mage",
                    "Unique Avatar", "Data Hog", "On the Wagon", "Escaped Custody", "Delicate Fingers",
                    "Revenant Adept", "Skinwalker", "Spell Jammer", "Spectral Warden",
                    "Code of Honor: Black Hat", "Know Your Limit", "Sprite Combustion", "Taint of Dissonance", "Wired User",
                    "Spirit Hunter I", "Spirit Hunter II", "Spirit Hunter III", "Spiritual Pilgrim",
                    "Stalwart Ally", "Taboo Transformer", "Worship Leader", "Charlatan", "Chosen Follower", "Vexcraft",
                    "Spiritual Lodge", "Sprawl Tamer", "Crystalline Diver", "Crystalline Grace",
                    "Blood Necromancer", "Chakra Interrupter", "Close Combat Mage", "Dark Ally",
                    "Dissonant Stream: Apophenian", "Dissonant Stream: Erisian", "Dissonant Stream: Morphinae" })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == name);
                    if (name == "Mnemonic Vault")
                        Require(quality.Element("required")!.Element("oneof")!.Element("quality")!.Value == "Memory Palace",
                            "Memory help must retain its prerequisite quality.");
                    else if (name == "Escaped Custody")
                        Require(quality.Element("required")!.Element("allof")!.Elements("quality")
                                .Select(q => q.Value).SequenceEqual(new[] { "Records on File", "Technomancer" }),
                            "Escaped Custody retains both corporate records and technomancer admission.");
                    else if (name == "Delicate Fingers")
                        Require(quality.Element("required")!.Element("oneof")!.Element("metatype")!.Value == "Troll",
                            "Equipment handling help must retain its troll prerequisite.");
                    else if (name == "Wired User")
                        Require(quality.Element("required")!.Element("allof")!.Element("quality")!.Value == "Technomancer"
                            && quality.Element("required")!.Element("oneof")!.Elements("quality")
                                .Select(q => q.Value).SequenceEqual(new[] { "Addiction (Mild)", "Addiction (Moderate)",
                                    "Addiction (Severe)", "Addiction (Burnout)" }),
                            "Wired User help must retain both technomancer and addiction admission.");
                    else if (name is "Brilliant Heuristics" or "Groveler" or "Hold the Door"
                        or "Fractal Punch" or "Lone Wolf" or "Team Player" or "Natural Hacker"
                        or "One With the Matrix I" or "One With the Matrix II" or "One With the Matrix III"
                        or "Sprite Affinity" or "Trust Data, Not Lore" or "Trust Lore, Not Data"
                        or "Unique Avatar" or "Data Hog" or "On the Wagon"
                        or "Code of Honor: Black Hat" or "Know Your Limit" or "Sprite Combustion" or "Taint of Dissonance"
                        or "Dissonant Stream: Apophenian" or "Dissonant Stream: Erisian" or "Dissonant Stream: Morphinae")
                        Require(quality.Element("required")!.Element("oneof")!.Element("quality")!.Value == "Technomancer",
                            "Resonance help must retain technomancer admission: " + name);
                    else
                        Require(quality.Element("required")!.Element("allof")!.Element("magenabled") is not null,
                            "Mastery help must retain magical admission: " + name);
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.Element("required")!.Remove();
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var changedLines = CreationQualityInfo.Effects(changed.ToString());
                    Require(!changedLines.Contains(summary)
                        && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                        "Bound help must reject a definition without its prerequisites: " + name);
                }
                foreach (var prerequisite in new[]
                {
                    (Name: "Designer", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Hello World!", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Hello World!", Path: "limit", Value: "3"),
                    (Name: "Persnickety Renter", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Real World Naiveté", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Charlatan", Path: "required/allof/skill/name", Value: "Assensing"),
                    (Name: "Chosen Follower", Path: "required/oneof/quality", Value: "Mentor Spirit"),
                    (Name: "Vexcraft", Path: "required/oneof/skill/name", Value: "Disenchanting"),
                    (Name: "Hair Trigger", Path: "required/oneof/quality", Value: "Technomancer"),
                    (Name: "Revenant Adept", Path: "required/oneof/power", Value: "Rapid Healing"),
                    (Name: "Skinwalker", Path: "required/allof/spell", Value: "[Critter] Form"),
                    (Name: "Spell Jammer", Path: "required/oneof/skill/name", Value: "Counterspelling"),
                    (Name: "Wired User", Path: "required/oneof/quality", Value: "Addiction (Mild)"),
                    (Name: "Spirit Hunter II", Path: "required/allof/quality", Value: "Spirit Hunter I"),
                    (Name: "Spirit Hunter III", Path: "required/allof/quality", Value: "Spirit Hunter II"),
                    (Name: "Improved Restoration", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Low Profile", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Munge", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Multiprocessing", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Centaur Body", Path: "required/allof/metatype", Value: "Centaur"),
                    (Name: "Latent Dracomorphosis", Path: "forbidden/oneof/quality", Value: "Dracoform (Eastern Drake)"),
                    (Name: "Conjuring Geas", Path: "careeronly", Value: ""),
                    (Name: "Conjuring Geas", Path: "bonus/astralreputation", Value: "-1"),
                    (Name: "The Twisted Way", Path: "required/oneof/quality", Value: "Adept"),
                    (Name: "The Twisted Way", Path: "forbidden/oneof/quality", Value: "The Artisan's Way"),
                    (Name: "It Works If You Work It", Path: "required/oneof/quality", Value: "Infected: Bandersnatch"),
                    (Name: "Metaviral Attunement", Path: "required/oneof/quality", Value: "Infected: Bandersnatch"),
                    (Name: "Soul Swallower", Path: "required/oneof/critterpower", Value: "Essence Drain"),
                    (Name: "Stalwart Ally", Path: "required/allof/spell", Value: "Create Ally Spirit"),
                    (Name: "Taboo Transformer", Path: "required/oneof/group/spell", Value: "Shapechange"),
                    (Name: "Worship Leader", Path: "required/allof/skill/name", Value: "Leadership"),
                    (Name: "Sapper", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Sensor Upgrade", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Snooper", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Virtual Stability", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Easily Exploitable", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Easily Exploitable", Path: "implemented", Value: "False"),
                    (Name: "Corrupter", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Decaying Dissonance", Path: "required/oneof/quality", Value: "Technomancer"),
                    (Name: "Wanted by GOD", Path: "forbidden/oneof/quality", Value: "Technomancer"),
                    (Name: "Wanted by GOD", Path: "required/oneof/skill/name", Value: "Hacking"),
                    (Name: "Spiritual Lodge", Path: "required/allof/skill/name", Value: "Ritual Spellcasting"),
                    (Name: "Sprawl Tamer", Path: "required/oneof/skill/name", Value: "Animal Handling"),
                    (Name: "Crystalline Diver", Path: "required/oneof/quality", Value: "Crystal Breath"),
                    (Name: "Crystalline Grace", Path: "required/oneof/quality", Value: "Crystal Limb (Leg)"),
                    (Name: "Busted Cyberware", Path: "chargenonly", Value: ""),
                    (Name: "Busted Cyberware", Path: "bonus/addware/name", Value: "Busted Ware"),
                    (Name: "Pilot Origins", Path: "required/oneof/metatype", Value: "A.I."),
                    (Name: "Blood Necromancer", Path: "required/allof/metamagicart", Value: "Blood Magic"),
                    (Name: "Chakra Interrupter", Path: "required/oneof/group/power", Value: "Nerve Strike"),
                    (Name: "Close Combat Mage", Path: "required/oneof/metamagic", Value: "Spell Shaping"),
                    (Name: "Dark Ally", Path: "bonus/addspirit", Value: ""),
                    (Name: "People's SIN", Path: "forbidden/oneof/quality", Value: "SINner (Corporate)"),
                    (Name: "People's SIN (Criminal)", Path: "forbidden/oneof/quality", Value: "SINner (Corporate)"),
                    (Name: "Elemental Attunement", Path: "required/allof/power", Value: "Killing Hands")
                })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == prerequisite.Name);
                    var changed = new System.Xml.Linq.XElement(quality);
                    var node = prerequisite.Path.Split('/').Aggregate(changed, (parent, name) => parent.Element(name)!);
                    Require(node.Value == prerequisite.Value, "Focused prerequisite fixture changed: " + prerequisite.Name);
                    node.Remove();
                    Require(!CreationQualityInfo.Effects(changed.ToString()).Contains(
                        CreationFlowStrings.Get(SummaryKey(quality), "")),
                        "Removing a specific prerequisite must invalidate bound help: " + prerequisite.Name);
                }
                foreach (string name in new[] { "Dead Emotion", "Favored (Common Target, Biased)",
                    "Favored (Common Target, Outspoken)", "Favored (Common Target, Fanatic)",
                    "Favored (Specific Target, Biased)", "Favored (Specific Target, Outspoken)",
                    "Favored (Specific Target, Fanatic)", "Illusionist",
                    "Dry Addict (Mild)", "Dry Addict (Moderate)", "Dry Addict (Severe)", "Dry Addict (Burnout)",
                    "Natural Hacker", "Corporate Loyalist", "Metaviral Attunement", "Persnickety Renter",
                    "Busted Cyberware", "Pilot Origins", "Close Combat Mage",
                    "Phenotypic Variation - Cosmetic Alteration", "Phenotypic Variation - Metaposeur",
                    "Location Attunement I", "Location Attunement II", "Location Attunement III",
                    "Escaped Custody", "Rank (Neither Military nor Law Enforcement) I",
                    "Rank (Neither Military nor Law Enforcement) II", "Rank (Neither Military nor Law Enforcement) III",
                    "Rank (Military or Law Enforcement) I", "Rank (Military or Law Enforcement) II",
                    "Rank (Military or Law Enforcement) III" })
                {
                    var selected = catalog.Single(q => q.Element("name")!.Value == name);
                    Require(selected.Element("bonus")!.Element("selecttext") is not null,
                        "Choice-bound help must preserve the player's specific selection: " + name);
                    var changed = new System.Xml.Linq.XElement(selected);
                    changed.Element("bonus")!.Element("selecttext")!.Remove();
                    Require(!CreationQualityInfo.Effects(changed.ToString()).Contains(
                        CreationFlowStrings.Get(SummaryKey(selected), "")),
                        "Choice-bound help must not survive removal of its choice: " + name);
                }
                var grantedThrillSeeker = catalog.Single(q => q.Element("name")!.Value
                    == "Poor Self Control (Thrill Seeker) (Dareadrenaline)");
                Require(grantedThrillSeeker.Element("hide") is not null
                    && grantedThrillSeeker.Element("karma")!.Value == "0",
                    "Augmentation-granted drawback help must not turn it into a purchasable Karma reward.");
                var changedGrantedThrillSeeker = new System.Xml.Linq.XElement(grantedThrillSeeker);
                changedGrantedThrillSeeker.Element("hide")!.Remove();
                Require(!CreationQualityInfo.Effects(changedGrantedThrillSeeker.ToString()).Contains(
                    CreationFlowStrings.Get(SummaryKey(grantedThrillSeeker), "")),
                    "Removing hidden-grant status must invalidate the old explanation.");
                var centaurBody = catalog.Single(q => q.Element("name")!.Value == "Centaur Body");
                Require(centaurBody.Element("hide") is not null && centaurBody.Element("karma")!.Value == "0",
                    "An inherited centaur trait must not be presented as a separately purchased advantage.");
                var changedCentaurBody = new System.Xml.Linq.XElement(centaurBody);
                changedCentaurBody.Element("hide")!.Remove();
                Require(!CreationQualityInfo.Effects(changedCentaurBody.ToString()).Contains(
                    CreationFlowStrings.Get(SummaryKey(centaurBody), "")),
                    "Removing inherited-trait visibility restrictions must reject its old explanation.");
                foreach (string stream in new[] { "Apophenian", "Erisian", "Morphinae" })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == "Dissonant Stream: " + stream);
                    var exclusions = quality.Element("forbidden")!.Element("oneof")!.Elements("quality")
                        .Select(q => q.Value).ToArray();
                    Require(exclusions.Length == 6 && exclusions.Distinct().Count() == 6
                        && new[] { "Cyberadept", "Machinist", "Sourceror", "Technoshaman" }
                            .All(name => exclusions.Contains("Resonant Stream: " + name))
                        && new[] { "Apophenian", "Erisian", "Morphinae" }.Where(name => name != stream)
                            .All(name => exclusions.Contains("Dissonant Stream: " + name)),
                        "Dissonant-stream help must retain exclusive stream admission.");
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.Element("forbidden")!.Remove();
                    Require(!CreationQualityInfo.Effects(changed.ToString()).Contains(
                        CreationFlowStrings.Get(SummaryKey(quality), "")),
                        "Removing stream exclusions must reject the old explanation.");
                }
                var spriteAffinity = catalog.Single(q => q.Element("name")!.Value == "Sprite Affinity");
                Require(spriteAffinity.Element("bonus")!.Element("selectsprite") is not null,
                    "Sprite Affinity help must retain the choice of sprite type.");
                var changedSpriteAffinity = new System.Xml.Linq.XElement(spriteAffinity);
                changedSpriteAffinity.Element("bonus")!.Element("selectsprite")!.Remove();
                Require(!CreationQualityInfo.Effects(changedSpriteAffinity.ToString()).Contains(
                    CreationFlowStrings.Get(SummaryKey(spriteAffinity), "")),
                    "Removing the sprite type choice must invalidate its bound help.");
                foreach (var choice in new[]
                {
                    (Name: "Natural Hacker", File: "actions.xml",
                        Path: "/chummer/actions/action[category = 'Matrix' and type != 'No']/name"),
                    (Name: "Phenotypic Variation - Metaposeur", File: "metatypes.xml",
                        Path: "/chummer/metatypes/metatype | /chummer/metatypes/metatype/metavariants/metavariant")
                })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == choice.Name);
                    var selector = quality.Element("bonus")!.Element("selecttext")!;
                    Require(selector.Attribute("xml")!.Value == choice.File
                        && selector.Attribute("xpath")!.Value == choice.Path,
                        "Choice help must retain its exact action or metatype scope: " + choice.Name);
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.Element("bonus")!.Element("selecttext")!.SetAttributeValue("xpath", "/custom");
                    Require(!CreationQualityInfo.Effects(changed.ToString()).Contains(
                        CreationFlowStrings.Get(SummaryKey(quality), "")),
                        "A changed selection scope must invalidate the old explanation: " + choice.Name);
                }
                foreach (var grade in new[]
                {
                    (Name: "One With the Matrix I", Excluded: new[] { "One With the Matrix III" }),
                    (Name: "One With the Matrix II", Excluded: new[] { "One With the Matrix III" }),
                    (Name: "One With the Matrix III", Excluded: new[] { "One With the Matrix I", "One With the Matrix II" }),
                    (Name: "Trust Data, Not Lore", Excluded: new[] { "Trust Lore, Not Data" }),
                    (Name: "Trust Lore, Not Data", Excluded: new[] { "Trust Data, Not Lore" }),
                    (Name: "Reverberant", Excluded: new[] { "Technomancer" }),
                    (Name: "Corrosive Spit", Excluded: new[] { "Natural Venom" }),
                    (Name: "Unique Avatar", Excluded: new[] { "Digital Doppelganger" }),
                    (Name: "On the Wagon", Excluded: new[] { "Addiction (Mild)", "Addiction (Moderate)",
                        "Addiction (Severe)", "Addiction (Burnout)" })
                })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == grade.Name);
                    Require(quality.Element("forbidden")!.Element("oneof")!.Elements("quality")
                            .Select(q => q.Value).SequenceEqual(grade.Excluded),
                        "Quality help must retain incompatible qualities: " + grade.Name);
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.Element("forbidden")!.Remove();
                    Require(!CreationQualityInfo.Effects(changed.ToString()).Contains(
                        CreationFlowStrings.Get(SummaryKey(quality), "")),
                        "Removing quality exclusions must reject old help: " + grade.Name);
                }
                var frostbite = catalog.Single(q => q.Element("name")!.Value == "Frostbite");
                Require(frostbite.Element("bonus")!.Element("selectskill")!.Attribute("limittoskill")!.Value
                    == "Compiling,Computer,Cybercombat,Decompiling,Electronic Warfare,Hacking,Registering,Software",
                    "Conditional skill help must retain its accepted selection scope.");
                var changedFrostbite = new System.Xml.Linq.XElement(frostbite);
                changedFrostbite.Element("bonus")!.Element("selectskill")!.SetAttributeValue("limittoskill", "Running");
                Require(!CreationQualityInfo.Effects(changedFrostbite.ToString()).Contains(
                    CreationFlowStrings.Get(SummaryKey(frostbite), "")),
                    "Changing the eligible skills must invalidate the bound explanation.");
                foreach (string name in new[] { "Biosonar", "Frog Tongue", "Greasy Skin",
                    "Animal Pelage (Quills)", "Animal Pelage (Camo Fur)", "Magic Sense",
                    "Corrosive Spit", "Defensive Secretion", "Thermal Sensitivity" })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == name);
                    Require(quality.Element("metagenic")?.Value == "True"
                        && quality.Element("required")!.Element("oneof")!.Elements("quality")
                            .Select(q => q.Value).SequenceEqual(new[] { "Changeling (Class I SURGE)",
                                "Changeling (Class II SURGE)", "Changeling (Class III SURGE)" }),
                        "Sensory/morphology help must preserve its accepted SURGE prerequisite: " + name);
                    Require((quality.Element("required")!.Element("oneof")!.Element("metatype")?.Value == "Centaur")
                        == (name == "Magic Sense"),
                        "Magic Sense also admits centaurs; do not replace this quality with the same-named adept power.");
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.Element("required")!.Remove();
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var changedLines = CreationQualityInfo.Effects(changed.ToString());
                    Require(!changedLines.Contains(summary)
                        && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                        "Source-bound SURGE help must not survive removal of its prerequisite: " + name);
                }
                foreach (string name in new[] { "Phenotypic Variation - Genewipe",
                    "Phenotypic Variation - Masque", "Phenotypic Variation - Reprint",
                    "Phenotypic Variation - Shuffle", "Phenotypic Variation - Cosmetic Alteration",
                    "Phenotypic Variation - Print Removal", "Phenotypic Variation - Metaposeur", "Prototype Materials" })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == name);
                    string admission = name == "Prototype Materials" ? "forbidden" : "chargenonly";
                    Require(quality.Element(admission) is not null,
                        "Natural phenotype and mundane prototype help must retain their admission boundary: " + name);
                    if (name == "Prototype Materials")
                        Require(quality.Element("forbidden")!.Element("oneof")!.Element("magenabled") is not null
                            && quality.Element("forbidden")!.Element("oneof")!.Element("resenabled") is not null
                            && quality.Element("required")!.Element("oneof")!.Element("quality")!.Value == "Special Modifications",
                            "Prototype Materials expands Special Modifications only for mundane characters.");
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.Element(admission)!.Remove();
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var changedLines = CreationQualityInfo.Effects(changed.ToString());
                    Require(!changedLines.Contains(summary)
                        && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                        "Removing admission restrictions must reject old source-bound help: " + name);
                }
                foreach (var aptitude in new[] { (Name: "Aware", Skills: "Aware"),
                    (Name: "Explorer", Skills: "Explorer"), (Name: "Enchanter", Skills: "Enchanting") })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == aptitude.Name);
                    Require(quality.Element("onlyprioritygiven") is not null
                        && quality.Element("bonus")!.Element("enableattribute")!.Element("name")!.Value == "MAG"
                        && quality.Element("bonus")!.Element("unlockskills")!.Value == aptitude.Skills
                        && quality.Element("forbidden")!.Element("oneof")!.Elements("quality")
                            .Any(q => q.Value == "Magician"),
                        "Aptitude help must not turn an exclusive creation grant into a purchasable full-magician upgrade.");
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.Element("bonus")!.Element("unlockskills")!.Value = "Magician";
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var changedLines = CreationQualityInfo.Effects(changed.ToString());
                    Require(!changedLines.Contains(summary)
                        && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                        "Aptitude restrictions must not describe a custom source that grants different magical skills.");
                }
                VerifyInsectSpiritSummaries(catalog, locale);
                foreach (var rule in BriefDrawbackSummaries)
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == rule.Name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    string[] scope = locale == "de-AT" ? rule.German : locale == "es-MX" ? rule.Spanish : rule.English;
                    // Keep attribute-based versus skill-category scope explicit in every language.
                    bool addiction = rule.Name.StartsWith("Addiction (", StringComparison.Ordinal);
                    Require(summary.Length > 0 && summary.Length <= (addiction ? 220 : 180)
                        && summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= (addiction ? 30 : 25)
                        && scope.All(term => summary.Contains(term, StringComparison.OrdinalIgnoreCase))
                        && string.Join(",", Regex.Matches(summary, @"[+−-]?\d+").Select(m => m.Value)) == rule.Numbers
                        && CreationQualityInfo.Effects(quality.ToString())[0] == summary,
                        "Brief drawback help lost its localized grade, scope or modifier: " + rule.Name);
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.SetElementValue("karma", "999");
                    var changedLines = CreationQualityInfo.Effects(changed.ToString());
                    Require(!changedLines.Contains(summary)
                        && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                        "Brief drawback help must reject changed source definitions: " + rule.Name);
                }
                VerifyInfectedSummaries(catalog, locale);
                VerifyOptionalPowerAndDrakeSummaries(catalog, critterPowerDefinitions, locale);
                foreach (var rule in SourceEffectQualitySummaries)
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == rule.Name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    string scope = locale == "de-AT" ? rule.German : locale == "es-MX" ? rule.Spanish : rule.English;
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 0 && summary.Contains(scope, StringComparison.Ordinal)
                        && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Short source-effect help lost its localized scope: " + rule.Name);
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.SetElementValue("karma", "999");
                    var changedLines = CreationQualityInfo.Effects(changed.ToString());
                    Require(!changedLines.Contains(summary)
                        && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                        "Authored help must not survive a changed rule definition: " + rule.Name);
                }
                var crystalScope = locale switch
                {
                    "de-AT" => (Essence: "Essenz", Monitors: "beiden Zustandsmonitoren", Fatigue: "Erschöpfung",
                        Ingested: "geschluckte", Injected: "injizierte", Inhaled: "eingeatmete",
                        Lifestyle: "Lebensstilkosten", Armor: "Panzerung", Initiative: "Initiative"),
                    "es-MX" => (Essence: "Esencia", Monitors: "ambos monitores", Fatigue: "fatiga",
                        Ingested: "ingeridas", Injected: "inyectadas", Inhaled: "inhaladas",
                        Lifestyle: "costes de vida", Armor: "armadura", Initiative: "Iniciativa"),
                    _ => (Essence: "Essence", Monitors: "both condition monitors", Fatigue: "fatigue",
                        Ingested: "swallowed", Injected: "injected", Inhaled: "inhaled",
                        Lifestyle: "lifestyle costs", Armor: "armor", Initiative: "Initiative")
                };
                var crystalQualities = catalog.Where(q => q.Element("name")!.Value.StartsWith("Crystal ", StringComparison.Ordinal)).ToArray();
                Require(crystalQualities.Length == 22, "Review new crystal variants rather than silently omitting their help.");
                foreach (var quality in crystalQualities)
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var bonus = quality.Element("bonus")!;
                    Require(summary.Contains(crystalScope.Essence) && summary.Contains(crystalScope.Monitors),
                        "Crystal help must disclose Essence and both condition-monitor costs.");
                    Require(bonus.Element("conditionmonitor")!.Element("physical")!.Value == "-1"
                        && bonus.Element("conditionmonitor")!.Element("stun")!.Value == "-1"
                        && decimal.Parse(bonus.Element("essencepenaltyt100")!.Value, CultureInfo.InvariantCulture)
                            + decimal.Parse(bonus.Element("essencepenaltymagonlyt100")!.Value, CultureInfo.InvariantCulture) == 0,
                        "Crystal source no longer supports the stated condition-monitor/Magic trade-off.");
                    foreach (var effect in new[]
                    {
                        (Tag: "fatigueresist", Token: crystalScope.Fatigue),
                        (Tag: "toxiningestionresist", Token: crystalScope.Ingested),
                        (Tag: "toxininjectionresist", Token: crystalScope.Injected),
                        (Tag: "toxininhalationresist", Token: crystalScope.Inhaled),
                        (Tag: "lifestylecost", Token: crystalScope.Lifestyle),
                        (Tag: "armor", Token: crystalScope.Armor),
                        (Tag: "initiative", Token: crystalScope.Initiative)
                    })
                        Require(summary.Contains(effect.Token, StringComparison.OrdinalIgnoreCase)
                            == (bonus.Element(effect.Tag) is not null),
                            "Crystal summary dropped a benefit or borrowed another variant's effect: "
                            + quality.Element("name")!.Value + "/" + effect.Tag);
                }
                var contactScope = locale switch
                {
                    "de-AT" => (Minimum: "mindestens", Recovery: "Wiedergutmachung"),
                    "es-MX" => (Minimum: "mínimo", Recovery: "reparar"),
                    _ => (Minimum: "minimum", Recovery: "amends")
                };
                foreach (var rule in new[] { (Name: "Candle in the Darkness", Numbers: "+2,−1", Scope: contactScope.Recovery),
                    (Name: "Massive Network", Numbers: "2,2", Scope: contactScope.Minimum),
                    (Name: "Networker", Numbers: "1,1", Scope: contactScope.Minimum) })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == rule.Name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 35 && lines[0] == summary && summary.Contains(rule.Scope)
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Contact qualities need complete localized inline help: " + rule.Name);
                    Require(string.Join(",", Regex.Matches(summary, @"[+−-]?\s*\d+")
                        .Select(m => Regex.Replace(m.Value, @"\s+", ""))) == rule.Numbers,
                        "A contact translation changed its numbers: " + rule.Name);
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.SetElementValue("karma", "999");
                    var changedLines = CreationQualityInfo.Effects(changed.ToString());
                    Require(!changedLines.Contains(summary)
                        && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                        "Contact help must reject changed definitions: " + rule.Name);
                }
                foreach (var pair in new[] { (Name: "Massive Network", Other: "Networker"),
                    (Name: "Networker", Other: "Massive Network") })
                    Require(CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == pair.Name)), "")
                        .Contains(pair.Other), "Contact help lost the excluded counterpart.");
                foreach (var rule in new[] { (Name: "Natural Leader", Numbers: "+1"),
                    (Name: "Observant", Numbers: ""), (Name: "Battle Hardened", Numbers: "+1,3"),
                    (Name: "Thousand-Yard Stare", Numbers: "−1,3"),
                    (Name: "Go Big or Go Home", Numbers: "3,−6,−10"), (Name: "I C U", Numbers: "+2"),
                    (Name: "Otaku to Technomancer", Numbers: "+2"), (Name: "Deck Builder", Numbers: "1"),
                    (Name: "Impenetrable Logic", Numbers: ""), (Name: "Silence is Golden", Numbers: "2,10") })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == rule.Name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 35 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Combat and Matrix qualities need their own inline explanation: " + rule.Name);
                    Require(string.Join(",", Regex.Matches(summary, @"[+−-]?\s*\d+")
                        .Select(m => Regex.Replace(m.Value, @"\s+", ""))) == rule.Numbers,
                        "Translation changed a combat or Matrix quality's numeric scope: " + rule.Name);
                    var changed = new System.Xml.Linq.XElement(quality);
                    changed.SetElementValue("karma", "999");
                    var changedLines = CreationQualityInfo.Effects(changed.ToString());
                    Require(!changedLines.Contains(summary)
                        && changedLines.Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")),
                        "A combat or Matrix explanation must not survive changed source bytes: " + rule.Name);
                }
                var combatMatrixScope = locale switch
                {
                    "de-AT" => (Teamwork: "Teamwork", Action: "Freien Handlung", Outside: "außerhalb", Physical: "sichtbar"),
                    "es-MX" => (Teamwork: "Trabajo en equipo", Action: "Acción Gratuita", Outside: "fuera", Physical: "físicamente"),
                    _ => (Teamwork: "Teamwork", Action: "Free Action", Outside: "outside", Physical: "physically")
                };
                foreach (var rule in new[] { (Name: "Natural Leader", Scope: combatMatrixScope.Teamwork),
                    (Name: "Observant", Scope: combatMatrixScope.Action),
                    (Name: "Silence is Golden", Scope: combatMatrixScope.Outside),
                    (Name: "I C U", Scope: combatMatrixScope.Physical) })
                    Require(CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == rule.Name)), "")
                        .Contains(rule.Scope, StringComparison.Ordinal),
                        "Translation dropped a combat or Matrix qualifier: " + rule.Name);
                foreach (var rule in new[] { (Name: "My Country, Right or Wrong", Numbers: ""),
                    (Name: "Cyber-snob", Numbers: "1"), (Name: "Implant-induced Immune Deficiency", Numbers: "5,−2"),
                    (Name: "Superhuman Psychosis", Numbers: ""), (Name: "Metahuman Traits", Numbers: "+1") })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == rule.Name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Implant, identity and loyalty qualities need translated inline explanations: " + rule.Name);
                    Require(string.Join(",", Regex.Matches(summary, @"[+−-]?\s*\d+")
                        .Select(m => Regex.Replace(m.Value, @"\s+", ""))) == rule.Numbers,
                        "Translation changed an implant, identity or loyalty quality's numeric scope: " + rule.Name);
                }
                var implantScope = locale switch
                {
                    "de-AT" => (Country: "Deinem Land", Snob: "Erschaffungsbeschränkungen", Immune: "körperliche",
                        Psychosis: "Ehrenkodex", Traits: "Attributsgrenzen"),
                    "es-MX" => (Country: "al país", Snob: "restricciones de creación", Immune: "física",
                        Psychosis: "Código de Honor", Traits: "límites de atributos"),
                    _ => (Country: "your country", Snob: "Creation restrictions", Immune: "physical",
                        Psychosis: "Code of Honor", Traits: "attribute limits")
                };
                foreach (var rule in new[] { (Name: "My Country, Right or Wrong", Scope: implantScope.Country),
                    (Name: "Cyber-snob", Scope: implantScope.Snob),
                    (Name: "Implant-induced Immune Deficiency", Scope: implantScope.Immune),
                    (Name: "Superhuman Psychosis", Scope: implantScope.Psychosis),
                    (Name: "Metahuman Traits", Scope: implantScope.Traits) })
                    Require(CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == rule.Name)), "")
                        .Contains(rule.Scope, StringComparison.Ordinal),
                        "A translated implant, identity or loyalty limitation disappeared: " + rule.Name);
                Require(CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == "My Country, Right or Wrong")), "")
                    != CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == "Deus Vult!")), ""),
                    "Shared wound mechanics must not collapse distinct loyalty obligations into one explanation.");
                foreach (var rule in new[] { (Name: "Deus Vult!", Numbers: ""),
                    (Name: "Code of Honor: Avenging Angel", Numbers: "1,−1,24"),
                    (Name: "Faceless", Numbers: "−2"),
                    (Name: "Illness", Numbers: ""),
                    (Name: "Pregnant", Numbers: "9") })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == rule.Name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Codes and conditional drawbacks need translated inline explanations: " + rule.Name);
                    Require(string.Join(",", Regex.Matches(summary, @"[+−-]?\s*\d+")
                        .Select(m => Regex.Replace(m.Value, @"\s+", ""))) == rule.Numbers,
                        "Translation changed a conditional drawback's modifier, interval or cost: " + rule.Name);
                }
                var conditionScope = locale switch
                {
                    "de-AT" => (Faith: "nächsten Kampf", Trust: "vertrauten", Illness: "Spielleitung kann",
                        Pregnancy: "Trimesterfolgen summieren sich"),
                    "es-MX" => (Faith: "siguiente combate", Trust: "confianza", Illness: "DJ puede",
                        Pregnancy: "Efectos acumulativos"),
                    _ => (Faith: "next combat", Trust: "trusted", Illness: "GM may",
                        Pregnancy: "Cumulative trimester effects")
                };
                foreach (var rule in new[] { (Name: "Deus Vult!", Scope: conditionScope.Faith),
                    (Name: "Faceless", Scope: conditionScope.Trust), (Name: "Illness", Scope: conditionScope.Illness),
                    (Name: "Pregnant", Scope: conditionScope.Pregnancy) })
                    Require(CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == rule.Name)), "")
                        .Contains(rule.Scope, StringComparison.Ordinal),
                        "A conditional quality lost its exception or limitation: " + rule.Name);
                foreach (var rule in new[] { (Name: "Tattoo Magic", Numbers: "2"),
                    (Name: "Spirit Champion", Numbers: ""), (Name: "Spirit Pariah", Numbers: ""),
                    (Name: "Gifted Healer", Numbers: ""),
                    (Name: "Strive For Perfection", Numbers: ""), (Name: "Barrens Rat", Numbers: "−1"),
                    (Name: "Elemental Focus", Numbers: "+2"), (Name: "Poisoner", Numbers: "+1") })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == rule.Name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Magic and specialist qualities need translated inline explanations: " + rule.Name);
                    Require(string.Join(",", Regex.Matches(summary, @"[+−-]?\s*\d+")
                        .Select(m => Regex.Replace(m.Value, @"\s+", ""))) == rule.Numbers,
                        "Translation changed a specialist's modifier, reagent cost or level limit: " + rule.Name);
                }
                var aged = catalog.Single(q => q.Element("name")!.Value == "Aged");
                var agedLines = CreationQualityInfo.Effects(aged.ToString());
                Require(aged.Element("limit")!.Value == "3"
                    && aged.Element("bonus")!.Element("knowledgeskillpoints")!.Element("val")!.Value == "5"
                    && agedLines.Contains(CreationFlowStrings.Get("Qualities.Effect.Knowledge skill points", "")
                        + " · " + CreationFlowStrings.Get("Qualities.Effect.Modifier", "") + ": 5")
                    && agedLines.Count(line => line.Contains(
                        CreationFlowStrings.Get("Qualities.Effect.Maximum change", "") + ": -1")) == 4,
                    "Short Aged prose must retain the exact per-level values in the supporting source effects.");
                var specialistScope = locale switch
                {
                    "de-AT" => (Tattoo: "weder diese Fertigkeiten oder Metamagien", Healer: "eine Aufgabe",
                        Aged: "Attributmaxima", Perfection: "außer bei Deckungsfeuer", Element: "Sekundäreffekte",
                        Conceal: "halbe Geschicklichkeit aufgerundet", Poison: "Giftresistenz steigt dadurch nicht"),
                    "es-MX" => (Tattoo: "no otorga esas habilidades, metamagias", Healer: "una tarea",
                        Aged: "máximos naturales", Perfection: "salvo en fuego de cobertura", Element: "efectos secundarios",
                        Conceal: "mitad de tu Agilidad redondeada hacia arriba", Poison: "No aumenta tu resistencia"),
                    _ => (Tattoo: "does not grant those skills or metamagics", Healer: "one task",
                        Aged: "natural physical-attribute maxima", Perfection: "except for covering fire", Element: "secondary effects",
                        Conceal: "half your Agility rounded up", Poison: "does not increase your resistance")
                };
                foreach (var rule in new[] { (Name: "Tattoo Magic", Scope: specialistScope.Tattoo),
                    (Name: "Gifted Healer", Scope: specialistScope.Healer), (Name: "Aged", Scope: specialistScope.Aged),
                    (Name: "Strive For Perfection", Scope: specialistScope.Perfection),
                    (Name: "Elemental Focus", Scope: specialistScope.Element),
                    (Name: "Barrens Rat", Scope: specialistScope.Conceal), (Name: "Poisoner", Scope: specialistScope.Poison) })
                    Require(CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == rule.Name)), "")
                        .Contains(rule.Scope, StringComparison.Ordinal),
                        "A specialist explanation lost its conditional scope: " + rule.Name);
                foreach (var quality in catalog.Where(q => q.Element("source")!.Value is "RG" or "R5"))
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Combat, vehicle and environmental qualities need inline help: " + quality.Element("name")!.Value);
                }
                foreach (var rule in new[] { (Name: "Brand Loyalty (Manufacturer)", Numbers: "1,1"),
                    (Name: "Brand Loyalty (Product)", Numbers: "1,1"), (Name: "Sharpshooter", Numbers: "2,1"),
                    (Name: "Radiation Sponge", Numbers: ""), (Name: "Rad-Tolerant", Numbers: "1"),
                    (Name: "Spacer", Numbers: "1"), (Name: "Earther", Numbers: "2"),
                    (Name: "Combat Junkie", Numbers: ""), (Name: "Chaser", Numbers: "2"),
                    (Name: "Dealer Connection", Numbers: ""), (Name: "Grease Monkey", Numbers: "1"),
                    (Name: "Speed Demon", Numbers: "1,3,4"), (Name: "Stunt Driver", Numbers: "2"),
                    (Name: "Subtle Pilot", Numbers: ""), (Name: "Motion Sickness", Numbers: ""),
                    (Name: "Too Much Data", Numbers: "4,2"), (Name: "Accident Prone", Numbers: "2"),
                    (Name: "Blighted (6 Months)", Numbers: "3,−1"),
                    (Name: "Blighted (12 Months)", Numbers: "3,−1"), (Name: "Blighted (24 Months)", Numbers: "3,−2,−1") })
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == rule.Name)), "");
                    Require(string.Join(",", Regex.Matches(summary, @"[+−-]?\s*\d+")
                        .Select(m => Regex.Replace(m.Value, @"\s+", ""))) == rule.Numbers,
                        "Combat/vehicle/environment translation changed modifiers or thresholds: " + rule.Name);
                }
                foreach (var group in new[] { new[] { "Brand Loyalty (Manufacturer)", "Brand Loyalty (Product)" },
                    new[] { "Blighted (6 Months)", "Blighted (12 Months)", "Blighted (24 Months)" },
                    new[] { "Radiation Sponge", "Rad-Tolerant" } })
                    Require(group.Select(name => CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == name)), ""))
                        .Distinct().Count() == group.Length,
                        "Different scopes or environmental drawbacks must not collapse to one generic description.");
                Require(CreationQualityInfo.Effects(catalog.Single(q => q.Element("name")!.Value == "One Trick Pony").ToString())
                    .Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "A summary of the technique grant must not hide the unresolved chosen technique's effects.");
                foreach (string name in new[] { "Astral Hazing", "Berserker", "Bioluminescence", "Cephalopod Skull",
                    "Cold-Blooded", "Symbiosis", "Adiposis", "Neoteny", "Slow Healer", "Stubby Arms" })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Conditional metagenic effects need translated, definition-bound explanations: " + name);
                }
                foreach (var rule in new[] { (Name: "Bioluminescence", Numbers: "1,1"),
                    (Name: "Cephalopod Skull", Numbers: "3"),
                    (Name: "Symbiosis", Numbers: ""), (Name: "Adiposis", Numbers: ""),
                    (Name: "Neoteny", Numbers: "2,10"), (Name: "Slow Healer", Numbers: "2"),
                    (Name: "Stubby Arms", Numbers: "1,1") })
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == rule.Name)), "");
                    Require(string.Join(",", Regex.Matches(summary, @"[+−-]?\s*\d+(?:[.,]\d+)?")
                        .Select(m => Regex.Replace(m.Value, @"\s+", "").Replace(',', '.'))) == rule.Numbers,
                        "Translation changed a conditional penalty, threshold, interval or movement rate: " + rule.Name);
                }
                var clarification = locale switch
                {
                    "de-AT" => (Astral: "Ausdehnung klärst du mit der Spielleitung", Social: "allergieartige Symptome"),
                    "es-MX" => (Astral: "Acuerda su alcance con el DJ", Social: "síntomas alérgicos"),
                    _ => (Astral: "extent requiring GM agreement", Social: "allergy-like symptoms")
                };
                Require(CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == "Astral Hazing")), "")
                    .Contains(clarification.Astral, StringComparison.Ordinal)
                    && CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == "Symbiosis")), "")
                    .Contains(clarification.Social, StringComparison.Ordinal),
                    "Brief help must retain uncertainty and conditional drawbacks without inventing numeric rules.");
                foreach (string name in new[] { "Critter Spook", "Cyclopean Eye", "Deformity (Picasso)",
                    "Deformity (Quasimodo)", "Feathers", "Insectoid Features", "Mood Hair", "Nocturnal",
                    "Scales", "Scent Glands", "Striking Skin Pigmentation", "Third Eye", "Unusual Hair", "Vestigial Tail" })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Metagenic drawbacks need translated, definition-bound consequences: " + name);
                }
                Require(new[] { "Deformity (Picasso)", "Deformity (Quasimodo)" }
                    .Select(name => CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == name)), ""))
                    .Distinct().Count() == 2,
                    "Facial and physical deformities affect different tests and must retain distinct explanations.");
                foreach (var rule in new[] { (Name: "Critter Spook", Numbers: "5,2"),
                    (Name: "Cyclopean Eye", Numbers: ""), (Name: "Deformity (Picasso)", Numbers: "−2"),
                    (Name: "Deformity (Quasimodo)", Numbers: "−2"), (Name: "Feathers", Numbers: "1"),
                    (Name: "Insectoid Features", Numbers: "1"), (Name: "Mood Hair", Numbers: "2"),
                    (Name: "Nocturnal", Numbers: "1"), (Name: "Scales", Numbers: ""),
                    (Name: "Scent Glands", Numbers: ""), (Name: "Striking Skin Pigmentation", Numbers: "2"),
                    (Name: "Third Eye", Numbers: "2"), (Name: "Unusual Hair", Numbers: "1"),
                    (Name: "Vestigial Tail", Numbers: "1") })
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == rule.Name)), "");
                    Require(string.Join(",", Regex.Matches(summary, @"[+−-]?\s*\d+")
                        .Select(m => Regex.Replace(m.Value, @"\s+", ""))) == rule.Numbers,
                        "Translation changed a metagenic drawback's value or conditional penalty: " + rule.Name);
                }
                foreach (string name in new[] { "Climate Adaptation (Arctic)", "Climate Adaptation (Desert)",
                    "Setae", "Monkey Paws", "Marsupial Pouch", "Electroception (Electrosense)",
                    "Electroception (Technosense)", "Proboscis", "Photometabolism" })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Body adaptations need translated, definition-bound benefits and limitations: " + name);
                }
                foreach (var pair in new[] {
                    new[] { "Climate Adaptation (Arctic)", "Climate Adaptation (Desert)" },
                    new[] { "Electroception (Electrosense)", "Electroception (Technosense)" } })
                {
                    Require(pair.Select(name => CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == name)), ""))
                        .Distinct().Count() == pair.Length,
                        "Environmental and electrical-sense variants must retain distinct explanations.");
                }
                foreach (var rule in new[] { (Name: "Climate Adaptation (Arctic)", Numbers: "1,1"),
                    (Name: "Climate Adaptation (Desert)", Numbers: "1,1"), (Name: "Monkey Paws", Numbers: "+2,+1"),
                    (Name: "Marsupial Pouch", Numbers: "−6"), (Name: "Proboscis", Numbers: ""),
                    (Name: "Photometabolism", Numbers: "10,1") })
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == rule.Name)), "");
                    string numbers = string.Join(",", Regex.Matches(summary, @"[+−-]?\s*\d+")
                        .Select(m => Regex.Replace(m.Value, @"\s+", "")));
                    Require(numbers == rule.Numbers,
                        "Translation lost or changed a body adaptation's signed value: " + rule.Name);
                }
                foreach (string name in new[] { "Electroception (Electrosense)", "Electroception (Technosense)" })
                    Require(CreationQualityInfo.Effects(catalog.Single(q => q.Element("name")!.Value == name).ToString())
                        .Any(line => line.Contains(CreationFlowStrings.Get(
                            "Qualities.Effect.Additional specialization option, not automatically learned", ""), StringComparison.Ordinal)
                            && line.Contains(CreationFlowStrings.Get("Qualities.Value.Electroception", ""), StringComparison.Ordinal)),
                        "An electrical-sense explanation must retain the distinction between an available and a learned specialization.");
                foreach (string name in new[] { "360-degree Eyesight", "Bicardiac",
                    "Broadened Auditory System (Infrasound)", "Broadened Auditory System (Ultrasound)",
                    "Camouflage", "Dynamic Coloration", "Gills (Air)", "Gills (Aqua)", "Gills (Full)",
                    "Glamour", "Keen-Eared", "Low-Light Vision (Changeling)", "Low-Light Vision (Feline)",
                    "Thermographic Vision (SURGE)", "Underwater Vision" })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Metagenic senses need translated, exact-definition explanations, including their limitations: " + name);
                }
                foreach (var group in new[] {
                    new[] { "Gills (Air)", "Gills (Aqua)", "Gills (Full)" },
                    new[] { "Camouflage", "Dynamic Coloration" },
                    new[] { "Broadened Auditory System (Infrasound)", "Broadened Auditory System (Ultrasound)" },
                    new[] { "Low-Light Vision (Changeling)", "Low-Light Vision (Feline)" } })
                {
                    Require(group.Select(name => CreationFlowStrings.Get(SummaryKey(catalog.Single(q => q.Element("name")!.Value == name)), ""))
                        .Distinct().Count() == group.Length,
                        "Sensory variants must not share misleading generic copy.");
                }
                foreach (var rule in new[] { (Name: "360-degree Eyesight", Numbers: ""),
                    (Name: "Camouflage", Numbers: ""), (Name: "Dynamic Coloration", Numbers: "2,4"),
                    (Name: "Glamour", Numbers: ""), (Name: "Keen-Eared", Numbers: "1") })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == rule.Name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    Require(string.Join(",", Regex.Matches(summary, @"\d+").Select(m => m.Value)) == rule.Numbers,
                        "Translation changed a sensory bonus, penalty, distance or duration: " + rule.Name);
                }
                foreach (string name in new[] { "Hung Out to Dry", "Night Blindness", "Paranoia",
                    "Vendetta", "Pie Iesu Domine. Dona Eis Requiem.",
                    "Carrier (HMHVV Strain II)", "Carrier (HMHVV Strain III)" })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Conditional drawbacks need translated explanations bound to the consumed definition: " + name);
                }
                var carrierSummaries = catalog.Where(q => q.Element("name")!.Value.StartsWith("Carrier (HMHVV", StringComparison.Ordinal))
                    .Select(q => CreationFlowStrings.Get(SummaryKey(q), "")).ToArray();
                Require(carrierSummaries.Length == 2 && carrierSummaries.Distinct().Count() == 2
                    && carrierSummaries.All(text => Regex.Matches(text, @"\d+").Select(m => m.Value).SequenceEqual(new[] { "2", "1" })),
                    "Carrier strains must remain distinct without losing their translated dice and reputation values.");
                foreach (string name in new[] { "Hawk Eye", "Jack of All Trades Master of None",
                    "Lightning Reflexes", "Linguist", "Sensei", "Trustworthy", "Witness My Hate",
                    "Illiterate", "Deaf" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Talents, training and sensory drawbacks need translated, definition-bound explanations: " + name);
                }
                var inspiredVariants = catalog.Where(q => q.Element("name")!.Value == "Inspired").ToArray();
                Require(inspiredVariants.Length == 2, "The consumed catalog has two distinct Inspired definitions.");
                foreach (var quality in inspiredVariants)
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary,
                        "Same-name qualities must retain their own translated, exact-definition summary.");
                }
                Require(inspiredVariants.Select(q => CreationFlowStrings.Get(SummaryKey(q), "")).Distinct().Count() == 2,
                    "Inspired's skill bonus must not overwrite its different expertise variant.");
                Require(CreationQualityInfo.Effects(catalog.Single(q => q.Element("name")!.Value == "Sensei").ToString())
                    .Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "Sensei's summary must not hide unresolved contact/skill selection details.");
                foreach (string name in new[] { "Phobia (Uncommon, Mild)", "Phobia (Uncommon, Moderate)",
                    "Phobia (Uncommon, Severe)", "Phobia (Common, Mild)", "Phobia (Common, Moderate)",
                    "Phobia (Common, Severe)", "Poor Self Control (Braggart)",
                    "Poor Self Control (Thrill Seeker)", "Poor Self Control (Vindictive)",
                    "Poor Self Control (Combat Monster)" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Fear and impulse variants need translated, definition-bound consequences: " + name);
                }
                var frequencyWords = locale switch
                {
                    "de-AT" => (Common: "häufiger", Uncommon: "seltener"),
                    "es-MX" => (Common: "frecuente", Uncommon: "raro"),
                    _ => (Common: "frequent", Uncommon: "rare")
                };
                foreach (string grade in new[] { "Mild", "Moderate", "Severe" })
                {
                    string common = CreationFlowStrings.Get(SummaryKey(catalog.Single(
                        q => q.Element("name")!.Value == $"Phobia (Common, {grade})")), "");
                    string uncommon = CreationFlowStrings.Get(SummaryKey(catalog.Single(
                        q => q.Element("name")!.Value == $"Phobia (Uncommon, {grade})")), "");
                    Require(common != uncommon && common.Replace(frequencyWords.Common, frequencyWords.Uncommon) == uncommon,
                        "Changing trigger frequency must not change the severity of the described fear response.");
                }
                foreach (string name in new[] { "Albinism I", "Albinism II",
                    "Amnesia (Surface Loss)", "Amnesia (Neural Deletion)",
                    "Day Job (10 hrs)", "Day Job (20 hrs)", "Day Job (40 hrs)", "In Debt",
                    "Incomplete Deprogramming", "Oblivious I", "Oblivious II",
                    "Pacifist I", "Pacifist II", "Records on File", "Sensory Overload Syndrome", "Wanted" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Drawback variants and obligations need translated, definition-bound explanations: " + name);
                }
                foreach (string name in new[] { "Asthma", "Big Regret", "Blind", "Borrowed Time",
                    "Computer Illiterate", "Creature of Comfort (Middle)", "Creature of Comfort (High)",
                    "Creature of Comfort (Luxury)", "Did You Just Call Me Dumb?", "Driven",
                    "Emotional Attachment", "Ex-Con", "Flashbacks I", "Flashbacks II",
                    "Hobo with a Shotgun", "Paraplegic", "Signature" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Run Faster drawbacks need translated, definition-bound consequences beyond names and selection prompts: " + name);
                }
                foreach (string name in new[] { "Adrenaline Surge", "Common Sense", "Daredevil",
                    "Digital Doppelganger", "Disgraced", "Night Vision", "Perfect Time", "Poor Link",
                    "Privileged Family Name", "Solid Rep", "Legendary Rep", "Speed Reading",
                    "Spike Resistance", "Spirit Whisperer", "Steely Eyed Wheelman" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Run Faster benefits need translated, definition-bound explanations, not empty bonus nodes or prompts: " + name);
                }
                foreach (var quality in catalog.Where(quality => quality.Element("source")?.Value == "SR5"))
                {
                    string name = quality.Element("name")!.Value;
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Every SR5 core-source entry needs an original, translated summary, including hidden granted traits: " + name);
                }
                foreach (string name in new[] { "Codeslinger", "Spirit Affinity",
                    "Infected Advanced Optional Power: Mimicry", "Infected Advanced Optional Power: Psychokinesis" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                        "A helpful summary must not hide unresolved action, spirit-picker or external-power details: " + name);
                }
                foreach (var quality in catalog.Where(quality => quality.Element("name")!.Value.StartsWith("SINner (", StringComparison.Ordinal)
                    || quality.Element("name")!.Value.StartsWith("Prejudiced (", StringComparison.Ordinal)))
                {
                    string name = quality.Element("name")!.Value;
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    bool unresolvedIssuerChoice = name is "SINner (Corporate)" or "SINner (Corporate Limited)";
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                        && lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")) == unresolvedIssuerChoice,
                        "SIN/prejudice help must explain each variant without concealing unresolved corporate issuer-picker details: " + name);
                    if (name.StartsWith("SINner (", StringComparison.Ordinal))
                    {
                        int tax = name == "SINner (Corporate)" ? 10 : name == "SINner (Corporate Limited)" ? 20 : 15;
                        Require(Regex.IsMatch(summary, $@"\b{tax}\s?%")
                            && Regex.Matches(summary, @"\b\d+\s?%").Count == 1,
                            "Every translated SIN variant must retain its own gross-income tax rate: " + name);
                    }
                    else
                    {
                        int dice = name.EndsWith("Biased)", StringComparison.Ordinal) ? 2
                            : name.EndsWith("Outspoken)", StringComparison.Ordinal) ? 4 : 6;
                        Require(summary.Contains($"{dice}", StringComparison.Ordinal)
                            && summary.Contains($"+{dice}", StringComparison.Ordinal),
                            "Translated prejudice help must retain both the player's penalty and the target's negotiation bonus: " + name);
                    }
                }
                foreach (var quality in catalog.Where(quality => quality.Element("name")!.Value.StartsWith("Allergy (", StringComparison.Ordinal)
                    || quality.Element("name")!.Value.StartsWith("Addiction (", StringComparison.Ordinal)))
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                        "Every allergy and addiction grade needs translated, definition-bound effects beyond its choice prompt or reputation.");
                }
                foreach (string name in new[] { "Astral Beacon", "Bad Luck", "Combat Paralysis", "Codeblock",
                    "Distinctive Style", "Insomnia (Basic)", "Insomnia (Full)", "Simsense Vertigo",
                    "Low Pain Tolerance", "Elf Poser", "Ork Poser", "Spirit Bane" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                        && lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")) == (name == "Spirit Bane"),
                        "Negative qualities need their own translated rules, not just reputation or a choice prompt: " + name);
                    // Spirit Bane's type picker references another catalog via
                    // selecttext attributes. Do not mark that unresolved detail
                    // complete just because its common rules now have prose.
                }
                var changedQuality = new System.Xml.Linq.XElement(catalog.Single(quality => quality.Element("name")!.Value == "Will to Live"));
                string originalSummary = CreationFlowStrings.Get("Qualities.Summary." + changedQuality.Element("id")!.Value, "");
                changedQuality.Element("bonus")!.Element("conditionmonitor")!.Element("overflow")!.Value = "4";
                var changedHelp = CreationQualityInfo.Effects(changedQuality.ToString());
                Require(!changedHelp.Contains(originalSummary),
                    "A house-rule definition retaining an official ID must not display the original one-box explanation.");
                Require(changedHelp[0] == CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")
                    && changedHelp.Any(line => line.Contains(": 4", StringComparison.Ordinal)),
                    "Changed definitions must explain the mismatch and retain their actual encoded values in every locale.");
                foreach (var original in catalog.Where(quality => CreationFlowStrings.Get(SummaryKey(quality), "").Length > 0))
                {
                    string prose = CreationFlowStrings.Get(SummaryKey(original), "");
                    Require(CreationQualityInfo.Effects(original.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))[0] == prose,
                        "Formatting alone must not discard a reviewed explanation.");
                    foreach (string field in new[] { "karma", "limit", "required", "bonus" })
                    {
                        var amended = new System.Xml.Linq.XElement(original);
                        amended.SetElementValue(field, "changed-definition");
                        var lines = CreationQualityInfo.Effects(amended.ToString());
                        Require(!lines.Contains(prose) && lines[0] == CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", ""),
                            "Same-ID changes to cost, limits, prerequisites or effects must not borrow reviewed prose: " + field);
                    }
                }
                foreach (string name in new[] { "Prototype Transhuman", "Wildcard Chimera",
                    "Resonant Stream: Technoshaman", "Resonant Stream: Cyberadept" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get("Qualities.Summary." + quality.Element("id")!.Value, "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 80 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                        && lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                        "Special choices need translated, useful help without concealing unexpanded rules: " + name);
                }
                string willToLive = catalog.Single(quality => quality.Element("name")!.Value == "Will to Live").ToString();
                var rated = CreationQualityInfo.Effects(willToLive, 3);
                string levelNotice = CreationFlowStrings.Format("Qualities.Info.BaseEffects", "missing", 3);
                Require(rated[1] == levelNotice && levelNotice.Contains("3")
                    && rated[2].Contains("1") && !rated[2].Contains("3")
                    && !CreationQualityInfo.Effects(willToLive, 1).Contains(levelNotice),
                    "A rated quality must distinguish its source values from the selected level, without Android multiplying them.");
                foreach (var quality in catalog.Where(quality => quality.Element("naturalweapons") is not null))
                {
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    foreach (var weapon in quality.Element("naturalweapons")!.Elements("naturalweapon"))
                    {
                        string weaponName = weapon.Element("name")!.Value;
                        string translated = CreationFlowStrings.Get("Qualities.Value." + weaponName, weaponName);
                        Require(lines.Any(line => line.StartsWith(CreationFlowStrings.Get("Qualities.Effect.Natural weapon", "") + ": " + translated,
                                StringComparison.Ordinal)), "Every encoded natural weapon must have a readable help entry in every locale.");
                    }
                }
                string fractionalEssence = string.Join(" ", CreationQualityInfo.Effects(
                    "<quality><bonus><essencepenaltyt100>-150</essencepenaltyt100></bonus></quality>"));
                Require(fractionalEssence.Contains((-1.5m).ToString(CultureInfo.CurrentUICulture))
                    && !fractionalEssence.Contains("-150"), "Hundredths must use readable, localized display units.");
                Console.WriteLine($"QUALITY_COPY locale={locale} catalog={catalog.Length} authored={authored} missing={missing} partial={partial}");
            }
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            string Effect(string name) => string.Join(" ", CreationQualityInfo.Effects(
                catalog.Single(quality => quality.Element("name")!.Value == name).ToString()));
            string EffectById(string id) => string.Join(" ", CreationQualityInfo.Effects(
                catalog.Single(quality => quality.Element("id")!.Value == id).ToString()));
            Require(Effect("Night Blindness").Contains("Worsens light/glare penalties, not normal lighting")
                && Effect("Night Blindness").Contains("Corrective implants require buying off this drawback")
                && Effect("Night Blindness").Contains("other eye-related qualities are incompatible"),
                "Night blindness needs its lighting exception and correction restriction.");
            Require(Effect("Paranoia").Contains("Loyalty is below 4")
                && Effect("Paranoia").Contains("relocate every few months")
                && Effect("Vendetta").Contains("Composure (3)")
                && Effect("Vendetta").Contains("buy it off, or a new enemy"),
                "Conditional social and feud drawbacks must retain thresholds, obligations and exit conditions.");
            Require(Effect("Hung Out to Dry").Contains("equal value")
                && Effect("Pie Iesu Domine. Dona Eis Requiem.").Contains("High Pain Tolerance 1")
                && Effect("Pie Iesu Domine. Dona Eis Requiem.").Contains("1 Physical damage box")
                && Effect("Pie Iesu Domine. Dona Eis Requiem.").Contains("compulsion"),
                "Removing a social drawback has a cost, and a granted benefit must not hide its recurring cost.");
            foreach (string strain in new[] { "II", "III" })
                Require(Effect($"Carrier (HMHVV Strain {strain})").StartsWith($"HMHVV-{strain} carrier:", StringComparison.Ordinal)
                    && Effect($"Carrier (HMHVV Strain {strain})").Contains("requires disease resistance")
                    && Effect($"Carrier (HMHVV Strain {strain})").Contains("Immune only")
                    && Effect($"Carrier (HMHVV Strain {strain})").Contains("aware of your status"),
                    "Carrier status is neither automatic infection, universal immunity nor a blanket social penalty.");
            Require(Effect("Hawk Eye").Contains("range penalties as one category nearer")
                && Effect("Hawk Eye").Contains("Incompatible with electronic vision enhancements")
                && Effect("Lightning Reflexes").Contains("+1 Initiative rating, +1 Initiative die")
                && Effect("Lightning Reflexes").Contains("do not stack with technological, chemical or magical enhancements"),
                "Natural vision and reflexes must retain their augmentation restrictions and distinct initiative values.");
            string inspiredTalent = EffectById("f8f216b5-1c29-467d-9fb5-c9812408203d");
            string inspiredExpertise = EffectById("fd9b9b6d-c969-40f1-8dc7-61f8e5d9cd4d");
            Require(inspiredTalent.Contains("Choose Artisan or Performance")
                && inspiredTalent.Contains("only among artists who know your reputation")
                && inspiredTalent.Contains("does not grant a specialization")
                && inspiredExpertise.StartsWith("Choose a free expertise specialization in Artisan.", StringComparison.Ordinal)
                && !inspiredExpertise.Contains("Street Cred"),
                "The two Inspired definitions share a name but not their skill choice, reputation or expertise benefits.");
            Require(Effect("Jack of All Trades Master of None").Contains("After creation")
                && Effect("Jack of All Trades Master of None").Contains("lower Active and Knowledge skill ranks cost less Karma")
                && Effect("Jack of All Trades Master of None").Contains("higher ranks costs more")
                && Effect("Linguist").Contains("half as long")
                && Effect("Linguist").Contains("At creation, language points buy twice as much")
                && Effect("Linguist").Contains("rating 3 or higher costs 1 less Karma"),
                "Training discounts must keep creation/Career boundaries and the higher-rating surcharge.");
            Require(Effect("Sensei").Contains("free teaching")
                && Effect("Sensei").Contains("one chosen skill or skill group")
                && Effect("Sensei").Contains("not free skill ranks")
                && Effect("Trustworthy").Contains("only when the situation involves trusting you"),
                "A teacher's skill ratings are not the runner's, and a trust-based limit is not a blanket social modifier.");
            Require(Effect("Witness My Hate").Contains("Single-target direct combat spells")
                && Effect("Witness My Hate").Contains("2 more damage but cause 2 more Drain")
                && Effect("Witness My Hate").Contains("does not improve indirect or area spells")
                && Effect("Illiterate").Contains("social, device and knowledge-skill use")
                && Effect("Illiterate").Contains("until you learn to read and buy off")
                && Effect("Deaf").Contains("audio-only Perception automatically fails")
                && Effect("Deaf").Contains("General Perception loses 2 dice; Surprise loses 3"),
                "Spell damage needs its Drain tradeoff; sensory drawbacks must retain their affected tests and recovery requirements.");
            Require(Effect("Codeslinger").Contains("one Matrix action that requires a test")
                && Effect("Codeslinger").Contains("two dice")
                && Effect("Home Ground").Contains("Only the selected benefit applies")
                && Effect("Home Ground").Contains("not every local bonus"),
                "Conditional action and home-ground benefits must not turn into blanket bonuses or bonus Karma.");
            foreach (string name in new[] { "Natural Immunity (Natural)", "Natural Immunity (Synthetic)" })
                Require(Effect(name).Contains("One exposure per 6 hours")
                    && Effect(name).Contains("further exposures cause normal damage with half-time recovery")
                    && Effect(name).Contains("Excludes magical agents"),
                    "Selected-agent immunity must retain its interval, repeated-exposure damage and magical-agent exclusion.");
            Require(Effect("Magician").Contains("does not grant every skill, spell or adept Power Points")
                && Effect("Aspected Magician").Contains("Choose exactly one group")
                && Effect("Aspected Magician").Contains("cannot project")
                && Effect("Astral Perception").Contains("mundane physical tasks lose two dice")
                && Effect("Astral Perception").Contains("pixie trait"),
                "Magic aptitude, an aspected skill group and a granted astral sense have different capabilities and costs.");
            Require(Effect("Low-Light Vision").Contains("total darkness still blocks")
                && Effect("Thermographic Vision").Contains("by one step")
                && Effect("Thermographic Vision").Contains("not a flat Perception bonus"),
                "Low-light and heat vision must retain distinct environmental limits.");
            Require(Effect("Spirit Affinity").Contains("extra service")
                && Effect("Spirit Affinity").Contains("easier Binding")
                && Effect("Spirit Affinity").Contains("no guaranteed obedience")
                && Effect("Infected Advanced Optional Power: Mimicry").Contains("voices or sounds, not appearances")
                && Effect("Infected Advanced Optional Power: Psychokinesis").Contains("hand's Strength and Agility, not your own attributes"),
                "Spirit and infected-power help must distinguish services, imitation and a telekinetic hand from personal attribute bonuses.");
            Require(Effect("Code of Honor").Contains("violations can cost Karma")
                && Effect("Scorched").Contains("neurological aftereffects")
                && Effect("Scorched").Contains("can trigger symptoms")
                && Effect("Scorched").Contains("medical treatment"),
                "Moral restrictions and neurological aftereffects need actual consequences beyond their selection prompt or reputation.");
            Require(Effect("Catlike").Contains("Sneaking") && Effect("Catlike").Contains("Bonus: 2"),
                "Specific-skill modifiers must be shown, not silently replaced by generic copy.");
            Require(Effect("Adrenaline Surge").Contains("opening Initiative Pass")
                && Effect("Adrenaline Surge").Contains("surprise still applies")
                && Effect("Common Sense").Contains("Edge rating in warnings per session")
                && Effect("Daredevil").Contains("recover 2 points instead of 1"),
                "Initiative priority must not remove surprise, warnings need their session cap, and recovered Edge is not maximum Edge.");
            Require(Effect("Digital Doppelganger").Contains("chosen real or eligible fake SIN")
                && Effect("Digital Doppelganger").Contains("Matrix searches struggle to trace")
                && Effect("Digital Doppelganger").Contains("other identities remain unprotected")
                && Effect("Disgraced").Contains("Criminals the GM considers susceptible")
                && Effect("Disgraced").Contains("easier to intimidate")
                && Effect("Disgraced").Contains("prejudice during Etiquette tests"),
                "Identity-scoped searches and intimidation benefits must retain their targets and social downside.");
            Require(Effect("Night Vision").Contains("daylight glare")
                && Effect("Night Vision").Contains("without a Karma refund")
                && Effect("Perfect Time").Contains("Free Action each Action Phase")
                && Effect("Perfect Time").Contains("not an extra attack"),
                "Night Vision needs its glare/loss drawbacks, and Perfect Time must not grant a full attack action.");
            Require(Effect("Poor Link").Contains("including beneficial rituals")
                && Effect("Privileged Family Name").Contains("minor local figures but makes you identifiable")
                && Effect("Privileged Family Name").Contains("national or full corporate SIN")
                && Effect("Solid Rep").Contains("one chosen group")
                && Effect("Legendary Rep").Contains("stronger than Solid Rep"),
                "Ritual resistance and local reputation must preserve their directions, identity requirements and distinct strengths.");
            Require(Effect("Speed Reading").Contains("Read quickly for general meaning")
                && Effect("Speed Reading").Contains("do not gain perfect recall")
                && Effect("Spike Resistance").Contains("one extra die per level")
                && Effect("Spike Resistance").Contains("does not increase Matrix armor")
                && catalog.Single(q => q.Element("name")!.Value == "Spike Resistance").Element("limit")!.Value == "3",
                "Reading speed must not become perfect recall, and biofeedback resistance is per level, not extra Matrix armor.");
            Require(Effect("Spirit Whisperer").Contains("Spirits resist summoning more strongly")
                && Effect("Spirit Whisperer").Contains("one Force stronger on success")
                && Effect("Spirit Whisperer").Contains("the summoning itself uses the declared Force")
                && Effect("Steely Eyed Wheelman").Contains("by 1, never below 0"),
                "The spirit's resistance bonus must not be given to its summoner, and reduced terrain penalties cannot become a bonus.");
            Require(Effect("Albinism I").Contains("Cybereye-compatible")
                && Effect("Albinism II").Contains("prior Karma downgrade")
                && Effect("Albinism II").Contains("precedence over other Karma spending")
                && Effect("Amnesia (Surface Loss)").Contains("retain practical abilities")
                && Effect("Amnesia (Surface Loss)").Contains("Knowledge skills, which cost Karma")
                && Effect("Amnesia (Neural Deletion)").Contains("GM controls")
                && Effect("Amnesia (Neural Deletion)").Contains("story progress and a Karma buyoff"),
                "Variant help must distinguish reduced symptoms and GM-mediated memory recovery without promising automatic app behavior.");
            foreach (var job in new[] { (Hours: 10, Pay: "1,000"), (Hours: 20, Pay: "2,500"), (Hours: 40, Pay: "5,000") })
                Require(Effect($"Day Job ({job.Hours} hrs)").Contains($"{job.Hours} weekly work hours earn ¥{job.Pay} monthly")
                    && Effect($"Day Job ({job.Hours} hrs)").Contains("fake rating four or higher")
                    && Effect($"Day Job ({job.Hours} hrs)").Contains("absence risks job, pay and reputation"),
                    "Short job help must retain its hours, salary, identity requirement and absence consequences.");
            Require(Effect("In Debt").Contains("150%") && Effect("In Debt").Contains("10% monthly")
                && Effect("In Debt").Contains("Missed payments cause lasting injury")
                && Effect("In Debt").Contains("still requires repayment")
                && Effect("Incomplete Deprogramming").Contains("Stress can reactivate")
                && Effect("Incomplete Deprogramming").Contains("During an episode")
                && Effect("Incomplete Deprogramming").Contains("skills become unavailable"),
                "Debts need ongoing obligations, and identity switches must not be described as permanent skill loss.");
            Require(new[] { "Oblivious I", "Oblivious II" }.All(name => Effect(name).Contains("astral and Matrix"))
                && Effect("Oblivious I").Contains("does not raise")
                && Effect("Oblivious II").Contains("thresholds by 1")
                && Effect("Pacifist I").Contains("ongoing attack")
                && Effect("Pacifist II").Contains("Reject all violence")
                && Effect("Pacifist II").Contains("lasting consequences")
                && Effect("Pacifist II").Contains("Believing you killed"),
                "Higher grades must preserve their stricter commitment and consequences without reproducing recovery procedures.");
            Require(Effect("Records on File").Contains("Their investigators gain advantages")
                && Effect("Records on File").Contains("identifying or locating you")
                && Effect("Records on File").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))
                && Effect("Sensory Overload Syndrome").Contains("sensory enhancements")
                && Effect("Sensory Overload Syndrome").Contains("temporary seizures")
                && Effect("Wanted").Contains("bounty attracts hunters")
                && Effect("Wanted").Contains("Karma buyoff"),
                "Investigators' advantages, unresolved corporation choices, timed overload and continuing bounty obligations must remain explicit.");
            foreach (string frequency in new[] { "Common", "Uncommon" })
            {
                string mild = Effect($"Phobia ({frequency}, Mild)");
                string moderate = Effect($"Phobia ({frequency}, Moderate)");
                string severe = Effect($"Phobia ({frequency}, Severe)");
                Require(mild.Contains("Exposure") && mild.Contains("mildly hinders all actions")
                    && !mild.Contains("flee") && !mild.Contains("flight")
                    && moderate.Contains("strongly hinders all actions") && moderate.Contains("flee unless you resist")
                    && severe.Contains("severely hinders all actions")
                    && severe.Contains("failed resistance forces sustained flight"),
                    "Fear penalties apply only in the trigger's presence; each grade has distinct resistance and flight behavior.");
                Require(mild.Contains(frequency == "Common" ? "frequent trigger" : "rare trigger"),
                    "Phobia frequency must remain distinct from severity.");
            }
            Require(Effect("Poor Self Control (Braggart)").Contains("struggle to stop boasting")
                && Effect("Poor Self Control (Thrill Seeker)").Contains("riskiest choice")
                && Effect("Poor Self Control (Thrill Seeker)").Contains("briefly improves your Initiative score")
                && Effect("Poor Self Control (Thrill Seeker)").Contains("not your Initiative dice")
                && Effect("Poor Self Control (Vindictive)").Contains("delaying retaliation does not remove")
                && Effect("Poor Self Control (Vindictive)").Contains("harsher revenge")
                && Effect("Poor Self Control (Combat Monster)").Contains("retreat takes self-control")
                && Effect("Poor Self Control (Combat Monster)").Contains("opponents remain able to fight"),
                "Impulse help must distinguish retained grudges, difficult withdrawal and a temporary initiative-score bonus.");
            Require(Effect("Asthma").Contains("twice as quickly")
                && Effect("Asthma").Contains("Accumulating fatigue")
                && Effect("Asthma").Contains("resistance to further exhaustion")
                && Effect("Asthma").Contains("action penalties, Social Limit"),
                "Asthma needs cumulative fatigue consequences, not an unconditional penalty or doubled damage claim.");
            Require(Effect("Big Regret").Contains("interactions with those who know it")
                && Effect("Big Regret").Contains("Exposure adds Notoriety and forces a Karma buyoff")
                && Effect("Blind").Contains("impairs perception and combat")
                && Effect("Blind").Contains("cybereyes cannot fix")
                && Effect("Blind").Contains("usual physical-action penalties"),
                "A secret's conditional social limit and blindness's distinct perception/astral consequences must remain clear.");
            Require(Effect("Borrowed Time").Contains("unpredictable death each session")
                && Effect("Borrowed Time").Contains("permanently sacrificing all current Edge")
                && Effect("Computer Illiterate").Contains("Computer, electronics and Matrix tests")
                && Effect("Computer Illiterate").Contains("double-counting penalties"),
                "Unavoidable mortality must not become ordinary Edge spending, and electronic penalties must not stack twice.");
            foreach (string tier in new[] { "Middle", "High", "Luxury" })
                Require(Effect($"Creature of Comfort ({tier})").Contains($"Below {tier} Lifestyle")
                    && Effect($"Creature of Comfort ({tier})").Contains($"per tier below {tier}"),
                    "Each comfort variant needs its own lifestyle baseline, not an accumulating per-day modifier.");
            Require(Effect("Did You Just Call Me Dumb?").Contains("critical glitches, even with hits")
                && Effect("Driven").Contains("temporarily strengthens Willpower")
                && Effect("Driven").Contains("endangers allies")
                && Effect("Emotional Attachment").Contains("permanent loss temporarily impairs related tests")
                && Effect("Emotional Attachment").Contains("buy off this drawback"),
                "Social glitches, conditional obsession benefits and lasting equipment loss need their actual consequences.");
            Require(Effect("Ex-Con").Contains("regular check-ins, police scrutiny")
                && Effect("Ex-Con").Contains("restrictions on implants and contacts")
                && Effect("Flashbacks I").Contains("roughly every other run")
                && Effect("Flashbacks II").Contains("at least once per session")
                && new[] { "Flashbacks I", "Flashbacks II" }.All(name =>
                    Effect(name).Contains("temporary incapacitation unless resisted")),
                "Parole requires real obligations; flashback grades change frequency, not the shared temporary incapacity and resistance.");
            Require(Effect("Hobo with a Shotgun").Contains("temporarily lowers all Mental attributes")
                && Effect("Hobo with a Shotgun").Contains("full day back at Squatter or Street")
                && Effect("Paraplegic").Contains("costlier living or vehicle adaptations")
                && Effect("Paraplegic").Contains("Astral and Matrix abilities are unaffected")
                && Effect("Signature").Contains("investigators")
                && Effect("Signature").Contains("connect you to jobs and track you"),
                "Lifestyle discomfort, mobility costs and identification modifiers must retain their affected actors and recovery conditions.");
            Require(Effect("Exceptional Attribute").Contains("Maximum change: 1")
                && Effect("Exceptional Attribute").Contains("Except: Edge"),
                "Nested attribute choice must retain its maximum and Edge exclusion.");
            Require(Effect("Will to Live").Contains("Additional overflow boxes: 1"),
                "Overflow boxes must not be confused with damage resistance.");
            Require(Effect("Magic Resistance").StartsWith("Each level adds one die when resisting spells.", StringComparison.Ordinal)
                && Effect("Magic Resistance").Contains("Spell resistance: 1"),
                "Magic Resistance needs a per-level spell-resistance explanation, not a spellcasting bonus.");
            Require(Effect("Resistance to Pathogens/Toxins").StartsWith("Add two dice", StringComparison.Ordinal)
                && Effect("Resistance to Pathogens and Toxins").StartsWith("Add one die", StringComparison.Ordinal)
                && Effect("Resistance to Pathogens and Toxins").Contains("never combine bonuses for alternative routes"),
                "The two distinct resistance sources must keep their exact values and must not sum alternative exposure routes.");
            Require(Effect("Born Rich").Contains("increases by 30") && Effect("Born Rich").Contains("still pay the Karma")
                && Effect("Out For Myself").Contains("three extra dice on Surprise tests"),
                "Readable explanations must retain an increased exchange limit and surprise dice, not free Karma or Initiative.");
            foreach (string name in new[] { "The Beast's Way", "The Spiritual Way", "The Burnout's Way", "The Magician's Way",
                "Changeling (Class I SURGE)", "Changeling (Class II SURGE)", "Changeling (Class III SURGE)",
                "Black Market Pipeline", "Erased", "Fame: Local", "Fame: National", "Fame: Megacorporate", "Fame: Global",
                "Made Man", "Ex-Con" })
            {
                string text = Effect(name);
                Require(!text.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                    && text.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "Special-rule summaries must explain supported effects without pretending their unencoded rules are complete: " + name);
            }
            Require(Effect("Erased").StartsWith("Your total Public Awareness is capped at 1.", StringComparison.Ordinal)
                && Effect("Erased").Contains("A lower value stays lower"),
                "Erased caps Public Awareness; it does not set it to one or reset all reputation.");
            Require(new[] { "The Beast's Way", "The Spiritual Way" }.All(name =>
                    Effect(name).Contains("Choosing Mentor Spirit costs no Karma")
                    && Effect(name).Contains("does not use the quality budget")
                    && Effect(name).Contains("eligible powers have limited discounts"))
                && Effect("The Beast's Way").Contains("Improve Animal Handling")
                && Effect("The Beast's Way").Contains("Skill · Animal Handling · Bonus: 1")
                && Effect("The Spiritual Way").Contains("Improve Conjuring")
                && Effect("The Spiritual Way").Contains("Skill group · Conjuring · Bonus: 1"),
                "A free-quality cost waiver is not an automatic mentor grant, and the two Ways have different skill bonuses.");
            Require(Effect("The Burnout's Way").Contains("Standard-grade implants cost less Essence")
                && Effect("The Burnout's Way").Contains("nuyen price is unchanged")
                && Effect("The Magician's Way").Contains("individual Power Point discounts")
                && Effect("The Magician's Way").Contains("excluded powers do not qualify"),
                "Adept Ways must not invent a blanket money or Power Point discount.");
            foreach (string name in new[] { "Changeling (Class I SURGE)", "Changeling (Class II SURGE)", "Changeling (Class III SURGE)" })
                Require(Effect(name).Contains("separate thirty-Karma limit")
                    && Effect(name).Contains("not extra general Karma")
                    && Effect(name).Contains("Only one SURGE class")
                    && catalog.Single(q => q.Element("name")!.Value == name)
                        .Element("bonus")!.Element("metageniclimit")!.Value == "30",
                    "The metagenic allowance must not be presented as general-purpose bonus Karma.");
            Require(Effect("Black Market Pipeline").Contains("Eligible purchases in that category receive a 10% price discount")
                && Effect("Made Man").Contains("group contact with Loyalty fixed at 3")
                && Effect("Ex-Con").Contains("also gain SINner (Criminal)"),
                "Contact and criminal-SIN explanations must retain their exact category, fixed Loyalty and grant boundaries.");
            foreach (var fame in new[] {
                (Name: "Fame: Local", Dice: 1, Limit: 1, Awareness: 2, Condition: "One chosen sprawl"),
                (Name: "Fame: National", Dice: 2, Limit: 1, Awareness: 3, Condition: "National language rating 4 or higher"),
                (Name: "Fame: Megacorporate", Dice: 2, Limit: 2, Awareness: 5, Condition: "One chosen megacorporation"),
                (Name: "Fame: Global", Dice: 3, Limit: 3, Awareness: 8, Condition: "") })
            {
                var quality = catalog.Single(q => q.Element("name")!.Value == fame.Name);
                var details = CreationQualityInfo.Effects(quality.ToString()).Skip(1).ToArray();
                Require(details.Contains($"Skill category · Social skills · Bonus: {fame.Dice}")
                    && details.Contains($"Public awareness: {fame.Awareness}")
                    && details.Contains($"Limit · Limit: Social · Modifier: {fame.Limit}"
                        + (fame.Condition.Length > 0 ? $" · When: {fame.Condition}" : "")),
                    "Short Fame prose must retain exact Core modifiers and conditional limits in its supporting profile: " + fame.Name);
            }
            Require(Effect("Quick Healer").Contains("Heal") && Effect("Quick Healer").Contains("Modifier: 2"),
                "Spell-specific healing modifier was lost.");
            Require(Effect("Uneducated").Contains("Cannot default")
                && Effect("Uneducated").Contains("Percent of normal cost: 200")
                && Effect("Uneducated").Contains("Specialization Karma cost"),
                "Skill restrictions and double training costs must be distinguished from bonus dice.");
            Require(Effect("Jack of All Trades Master of None").Contains("After character creation")
                && Effect("Jack of All Trades Master of None").Contains("Maximum: 5")
                && Effect("Jack of All Trades Master of None").Contains("Minimum: 6")
                && Effect("Jack of All Trades Master of None").Contains("Active-skill Karma cost change · Modifier: -1 · Maximum: 5")
                && Effect("Jack of All Trades Master of None").Contains("Active-skill Karma cost change · Modifier: 2 · Minimum: 6")
                && Effect("Jack of All Trades Master of None").Contains("Knowledge-skill Karma cost change · Modifier: -1 · Maximum: 5")
                && Effect("Jack of All Trades Master of None").Contains("Knowledge-skill Karma cost change · Modifier: 2 · Minimum: 6")
                && Effect("Jack of All Trades Master of None").Contains("Minimum Knowledge-skill Karma cost · Modifier: 1 · Maximum: 5")
                && !Effect("Jack of All Trades Master of None").Contains("/character/"),
                "Karma cost conditions and rating boundaries must be retained in readable language.");
            Require(Effect("Sensitive System").Contains("Cyberware Essence cost (% of normal): 200")
                && Effect("Sensitive System").Contains("Cannot use bioware"),
                "Essence multipliers and the bioware restriction were lost.");
            Require(Effect("Dependent (Nuisance)").Contains("Lifestyle cost change (%): 10"),
                "A lifestyle percentage must not be presented as nuyen or dice.");
            Require(Effect("Celerity").Contains("Replacement walking multiplier")
                && Effect("Celerity").Contains("Multiplier: 3")
                && Effect("Celerity").Contains("Replacement running multiplier")
                && Effect("Celerity").Contains("Multiplier: 6")
                && Effect("Celerity").Contains("Meters per hit: 1")
                && !Effect("Celerity").Contains("100"), "Replacement rates and extra sprint meters must not become 100 dice or a percentage.");
            Require(Effect("Satyr Legs").Contains("Meters per hit: 1")
                && !Effect("Satyr Legs").Contains("Replacement walking multiplier"),
                "Satyr Legs must not borrow Celerity's walking benefit.");
            Require(Effect("Consummate Professional").Contains("Extra earned Karma needed per Street Cred: 10")
                && Effect("Consummate Professional").Contains("per 20 Karma"),
                "Street Cred's extra Karma divisor is not a reputation multiplier.");
            Require(Effect("Resonant Burnout").Contains("Essence-related special-attribute loss (% of normal): 20"),
                "Reduced attribute loss must not be described as cheaper augmentation Essence.");
            Require(Effect("Barehanded Adept").Contains("Based on attribute: Magic")
                && Effect("Barehanded Adept").Contains("Half the rating, rounded up; touch range only")
                && Effect("Barehanded Adept").Contains("Permitted spell range: Touch (area)"),
                "Free spells must retain their Magic, rounding and Touch restrictions.");
            Require(Effect("Dedicated Spellslinger").Contains("Based on skill: Spellcasting")
                && Effect("Dedicated Spellslinger").Contains("Unavailable skill: Summoning")
                && Effect("Dedicated Spellslinger").Contains("Unavailable skill: Binding"),
                "Skill-based free spells must retain their unavailable magic skills.");
            string deadSin = Effect("Dead SIN");
            Require(deadSin.Contains("Granted equipment · Fake SIN") && deadSin.Contains("Rating: 3")
                && Regex.Matches(deadSin, "Included equipment · Fake License").Count == 4,
                "A granted SIN and four identical licenses must retain all four child items and their ratings.");
            string banshee = Effect("Infected: Banshee");
            Require(banshee.Contains("Granted power: Dual Natured") && banshee.Contains("Granted power: Allergy")
                && banshee.Contains("Fixed detail: Sunlight, Severe")
                && banshee.Contains("Choose from these powers, not all of them")
                && banshee.Contains("Number of choices: 1") && banshee.Contains("Power option: Enhanced Senses")
                && banshee.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Granted powers, selected details and optional choices must be distinguished; power names are not full rules.");
            Require(Effect("Electroception (Electrosense)").Contains("Additional specialization option, not automatically learned")
                && Effect("Electroception (Electrosense)").Contains("Specialization: Electroception")
                && Effect("Electroception (Electrosense)").Contains("Skill: Perception"),
                "A new specialization option must not be confused with automatically learning it.");
            string inspired(string id) => string.Join(" ", CreationQualityInfo.Effects(
                catalog.Single(quality => quality.Element("id")!.Value == id).ToString()));
            Require(inspired("fd9b9b6d-c969-40f1-8dc7-61f8e5d9cd4d").Contains("Choose a free expertise specialization")
                && !inspired("f8f216b5-1c29-467d-9fb5-c9812408203d").Contains("Choose a free expertise specialization"),
                "Same-name qualities must not share source-identity summaries or expertise effects.");
            foreach (var (attribute, name, impairedId, metagenicId, optimizedId) in new[]
            {
                ("BOD", "Body", "96911a4d-d82e-4a1e-a550-3e12b8e14a7b", "2ffd990c-ced6-4484-955c-108473df5335", "3109f474-0d10-4c75-bc07-ef22afdd92ab"),
                ("AGI", "Agility", "8a1a04ff-5bff-48b2-90c2-b9bdb5fde8e8", "96c4c8e1-2f5d-431c-96c8-db83820f747b", "c42cc305-3d7a-4c68-9ce7-32e1fc628505"),
                ("REA", "Reaction", "959b00a6-a55b-4c6b-95b0-a9746b31a24d", "c734c06d-c9e4-431d-93ba-033e82444efc", "8c667328-eba0-4dc7-be5a-19c285997f10"),
                ("STR", "Strength", "c00591dc-fe6d-4ff0-8e42-22000878f03f", "76ac03f1-b379-48a8-a6e8-d8611fce0adf", "a6b946e2-7e58-4607-b9ce-7b27055c9562"),
                ("CHA", "Charisma", "14e71856-7d38-4e04-933f-069ed89a14e9", "f1609199-e601-4b69-837f-2029b2ea94f0", "7022143c-3d58-4ad7-9cdb-dab007f75f54"),
                ("INT", "Intuition", "b58b47a7-9011-4bef-8ba2-ae561a443765", "7ee0765f-34f4-43ef-82c0-55c1b8bef8d8", "22a991a5-e8b1-4ca7-98b9-3181df5dbf7e"),
                ("LOG", "Logic", "0df94dda-5941-4130-b6fc-0a98fcf7f846", "bfdffed5-b687-4cb4-ad6a-ac59788fdc8a", "62035d8e-c784-450e-9b03-501281a6771a"),
                ("WIL", "Willpower", "a6a4d71d-452f-4d26-9588-e7a5bc2474c0", "a6536e32-5f2f-4865-8f6a-d2ba268d4647", "96ae02da-5824-4ebd-a0c5-65bb5b43fd48")
            })
            {
                var impaired = catalog.Single(quality => quality.Element("id")!.Value == impairedId);
                var metagenic = catalog.Single(quality => quality.Element("id")!.Value == metagenicId);
                var optimized = catalog.Single(quality => quality.Element("id")!.Value == optimizedId);
                foreach (var quality in new[] { impaired, metagenic, optimized })
                    Require(quality.Element("bonus")!.Element("specificattribute")!.Element("name")!.Value == attribute,
                        "An attribute explanation must bind to the matching source attribute, not merely a similar name.");
                Require(impaired.Element("bonus")!.Element("specificattribute")!.Element("max")!.Value == "-2"
                    && impaired.Element("bonus")!.Element("specificattribute")!.Element("min") is null
                    && inspired(impairedId).StartsWith($"Your natural maximum for {name} is reduced by 2.", StringComparison.Ordinal),
                    "Impaired Attribute lowers the maximum, not every roll or every attribute.");
                Require(metagenic.Element("bonus")!.Element("specificattribute")!.Element("max")!.Value == "1"
                    && metagenic.Element("bonus")!.Element("specificattribute")!.Element("min")!.Value == "1"
                    && metagenic.Element("required")!.Descendants("quality").Count() == 3
                    && inspired(metagenicId).StartsWith("SURGE changeling:", StringComparison.Ordinal)
                    && inspired(metagenicId).Contains($"natural minimum and maximum of {name} by one")
                    && inspired(metagenicId).Contains("Minimum change: 1")
                    && inspired(metagenicId).Contains("Maximum change: 1"),
                    "Metagenic Improvement raises both bounds and retains the changeling prerequisite.");
                Require(optimized.Element("bonus")!.Element("specificattribute")!.Element("max")!.Value == "1"
                    && optimized.Element("bonus")!.Element("specificattribute")!.Element("min") is null
                    && optimized.Element("chargenonly") is not null
                    && optimized.Element("forbidden")!.Descendants("bioware").Single().Value == $"Genetic Optimization ({name})"
                    && inspired(optimizedId).StartsWith($"Creation only: {name}'s natural maximum increases;", StringComparison.Ordinal)
                    && inspired(optimizedId).Contains("higher ratings still cost points")
                    && inspired(optimizedId).Contains("No Genetic Optimization bioware")
                    && inspired(optimizedId).Contains("same attribute"),
                    "Genetic Optimization raises only the ceiling, costs points to use, and keeps creation/bioware restrictions.");
            }
            Require(inspired("4e2ddf3d-802f-4206-85ce-81f1defa528f").StartsWith(
                    "Your Mental limit increases by 1 for Academic Knowledge tests.", StringComparison.Ordinal)
                && !inspired("4e2ddf3d-802f-4206-85ce-81f1defa528f").Contains("half")
                && inspired("604aea10-3f13-4f28-a87b-25b8bf677276").StartsWith(
                    "Academic Knowledge costs half the normal points and Karma during creation,", StringComparison.Ordinal)
                && inspired("604aea10-3f13-4f28-a87b-25b8bf677276").Contains("including Karma specializations")
                && inspired("604aea10-3f13-4f28-a87b-25b8bf677276").Contains("Career advances to rating 3+ cost 1 less Karma")
                && !inspired("604aea10-3f13-4f28-a87b-25b8bf677276").Contains("Mental limit increases"),
                "The two College Education identities have distinct limit versus training-cost rules.");
            foreach (string name in new[] { "School of Hard Knocks", "Technical School Education" })
            {
                var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                var bonus = quality.Element("bonus")!;
                Require(bonus.Element("skillcategorypointcostmultiplier")!.Element("val")!.Value == "50"
                    && bonus.Element("skillcategorykarmacost")!.Element("val")!.Value == "-1"
                    && bonus.Element("skillcategorykarmacost")!.Element("min")!.Value == "3"
                    && bonus.Element("skillcategorykarmacost")!.Element("condition")!.Value == "/character/created"
                    && bonus.Element("skillcategorykarmacostmultiplier") is null
                    && bonus.Element("skillcategoryspecializationkarmacostmultiplier") is null
                    && Effect(name).Contains("Career advances to rating 3+ cost 1 less Karma")
                    && Effect(name).Contains("not half the Karma"),
                    "Street/Professional discounts must not inherit the different Academic Karma discounts.");
            }
            Require(Effect("Incompetent").StartsWith("Choose one skill group that is unavailable to you.", StringComparison.Ordinal)
                && Effect("Incompetent").Contains("Notoriety also increases by 1")
                && Effect("Impassive").StartsWith("Your Social limit is 1 lower, except on Intimidation tests.", StringComparison.Ordinal),
                "Restrictions must retain the selected group, reputation penalty and Intimidation exception.");
            foreach (string name in new[]
            {
                "Animal Pelage (Insulating Pelt)",
                "Arcane Arrester",
                "Balance Receptor",
                "Beak",
                "Raptor Beak",
                "Dermal Alteration (Bark Skin)",
                "Dermal Alteration (Blubber)",
                "Dermal Alteration (Dragon Skin)",
                "Dermal Alteration (Granite Shell)",
                "Dermal Alteration (Rhino Hide)",
                "Dermal Deposits",
                "Elongated Limbs",
                "Functional Tail (Balance)",
                "Functional Tail (Paddle)",
                "Functional Tail (Prehensile)",
                "Magnetoception",
                "Ogre Stomach",
                "Photometabolism",
                "Thorns",
                "Vomeronasal Organ",
                "Webbed Digits",
                "Adiposis",
                "Deformity (Quasimodo)",
                "Neoteny",
                "Progeria",
                "Slow Healer",
                "Stubby Arms",
                "Social Appearance Anxiety",
                "Elevated Stress",
                "Sloppy Code",
                "Better on the Net [Attack]",
                "Brittle [Attack]",
                "Better on the Net [Data Processing]",
                "Brittle [Data Processing]",
                "Better on the Net [Firewall]",
                "Brittle [Firewall]",
                "Better on the Net [Sleaze]",
                "Brittle [Sleaze]"
            })
            {
                var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                string summary = CreationFlowStrings.Get("Qualities.Summary." + quality.Element("id")!.Value, "");
                Require(summary.Length > 40 && Effect(name).StartsWith(summary, StringComparison.Ordinal),
                    "Physical and Matrix traits need their own source-bound explanations: " + name);
            }
            foreach (var (name, field, improvedId, brittleId) in new[]
            {
                ("Attack", "attack", "522dbfb5-5ea0-4707-a7e8-1ac777b3aff7", "d40aee6b-80b7-43f5-b335-dabf8f1ad14d"),
                ("Data Processing", "dataprocessing", "39139f84-b8ab-4523-ac02-851350e3f2b6", "64d430b1-6d6a-4abc-9ef8-3ddaee2352ce"),
                ("Firewall", "firewall", "25d34c52-6927-44dd-85bd-684e9add1f89", "a1530804-a35b-4d2f-ae3e-50d647016903"),
                ("Sleaze", "sleaze", "59436ccc-5e4b-481d-8d63-d3ba912f2d8a", "cb5bd1e5-b9ef-434f-af17-0e7b4decaa46")
            })
            {
                foreach (var (id, amount, direction) in new[] { (improvedId, "2", "increases"), (brittleId, "-1", "decreases") })
                {
                    var quality = catalog.Single(quality => quality.Element("id")!.Value == id);
                    var persona = quality.Element("bonus")!.Element("livingpersona")!;
                    Require(persona.Elements().Count() == 1 && persona.Element(field)!.Value == amount
                        && quality.Element("required")!.Descendants("quality").Single().Value == "Technomancer"
                        && inspired(id).StartsWith($"Your living persona's {name} {direction} by {(amount == "-1" ? "1" : amount)}.", StringComparison.Ordinal)
                        && inspired(id).Contains("requires a technomancer"),
                        "Matrix help must preserve one exact attribute, direction, magnitude and technomancer requirement.");
                }
            }
            Require(Effect("Animal Pelage (Insulating Pelt)").Contains("4 to armor against cold")
                && Effect("Dermal Alteration (Blubber)").Contains("armor against cold only, not general armor")
                && Effect("Dermal Alteration (Blubber)").Contains("Cold resistance armor: 2")
                && Effect("Dermal Alteration (Dragon Skin)").Contains("armor against fire only, not general armor")
                && Effect("Dermal Alteration (Dragon Skin)").Contains("Fire resistance armor: 2"),
                "Cold and fire armor must not become universal armor or interchangeable elemental protection.");
            foreach (var (name, value) in new[] { ("Dermal Alteration (Bark Skin)", "2"),
                ("Dermal Alteration (Granite Shell)", "4"), ("Dermal Alteration (Rhino Hide)", "3") })
            {
                var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                Require(quality.Element("bonus")!.Element("armor")!.Value == value
                    && quality.Element("bonus")!.Element("armor")!.Attribute("group")!.Value == "0"
                    && Effect(name).Contains("adds armor, not Body or condition boxes")
                    && Effect(name).Contains("Other Dermal Alteration variants are incompatible")
                    && Effect(name).Contains("Armor: " + value)
                    && Effect(name).Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "Grouped armor may be explained, but its unexpanded stacking flag must still be marked partial.");
            }
            Require(Effect("Arcane Arrester").Contains("each level adds two spell-resistance dice, up to two levels")
                && Effect("Arcane Arrester").Contains("Incompatible with Magic Resistance")
                && Effect("Arcane Arrester").Contains("Spell resistance: 2")
                && Effect("Functional Tail (Balance)").Contains("Skill · Gymnastics · Bonus: 1")
                && Effect("Functional Tail (Paddle)").Contains("Skill · Swimming · Bonus: 2")
                && Effect("Functional Tail (Prehensile)").Contains("Skill · Gymnastics · Bonus: 1")
                && Effect("Vomeronasal Organ").Contains("smell-based Perception only"),
                "Spell resistance, alternative tail variants and scent bonuses must retain their distinct scopes.");
            Require(Effect("Beak").Contains("Lifestyle cost change (%): -10")
                && Effect("Beak").Contains("Ingested toxin resistance: 1")
                && Effect("Ogre Stomach").Contains("Lifestyle cost change (%): -20")
                && Effect("Ogre Stomach").Contains("Ingested toxin resistance: 2")
                && Effect("Raptor Beak").Contains("attack profile is not yet explained")
                && Effect("Raptor Beak").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Digestion bonuses must retain their cost/resistance differences, and weapon references stay incomplete.");
            Require(Effect("Adiposis").Contains("slows movement")
                && Effect("Adiposis").Contains("faster and more severe fatigue")
                && Effect("Adiposis").Contains("penalizes physical activities, including combat")
                && Effect("Adiposis").Contains("Exertion causes")
                && Effect("Thorns").Contains("unarmed damage, not attack dice")
                && Effect("Thorns").Contains("penalizes Physical Active tests")
                && Effect("Thorns").Contains("Unarmed damage change: 1")
                && catalog.Single(q => q.Element("name")!.Value == "Thorns")
                    .Element("bonus")!.Element("skillcategory")!.Element("bonus")!.Value == "-1",
                "Replacement movement rates and damage bonuses must not hide their skill penalties.");
            Require(Effect("Deformity (Quasimodo)").Contains("except Perception")
                && Effect("Neoteny").Contains("Physical condition monitor: 2 fewer boxes; Stun is unchanged")
                && Effect("Neoteny").Contains("increase lifestyle costs by 10%")
                && Effect("Slow Healer").Contains("including magical healing")
                && Effect("Slow Healer").Contains("do not combine Physical and Stun penalties")
                && Effect("Stubby Arms").Contains("Non-Combat tests requiring arm or hand dexterity lose 1 die"),
                "Physical drawbacks must preserve exclusions, monitor type and alternative healing rolls.");
            Require(Effect("Social Appearance Anxiety").Contains("one die per quality level, up to three levels")
                && Effect("Social Appearance Anxiety").Contains("When you are not looking your best")
                && Effect("Elevated Stress").Contains("only the relevant penalty, not every listed case together")
                && Effect("Sloppy Code").StartsWith("While inside a Matrix host,", StringComparison.Ordinal),
                "Conditional and rated penalties must not be advertised as unconditional combined totals.");
            var prototype = catalog.Single(quality => quality.Element("name")!.Value == "Prototype Transhuman");
            Require(prototype.Element("chargenonly") is not null
                && prototype.Element("bonus")!.Element("prototypetranshuman")!.Value == "1"
                && prototype.Element("bonus")!.Element("selectquality")!.Elements("quality").Select(choice => choice.Value)
                    .SequenceEqual(new[] { "Wanted", "Allergy (Common, Mild)", "Astral Beacon", "Insomnia (Basic)" })
                && Effect("Prototype Transhuman").Contains("Creation only: up to 1 Essence of prototype bioware")
                && Effect("Prototype Transhuman").Contains("still costs nuyen")
                && Effect("Prototype Transhuman").Contains("required drawback grants no extra Karma")
                && Effect("Prototype Transhuman").Contains("avoids Essence loss"),
                "Prototype help must preserve the creation-only allowance and mandatory drawback, not free augmentation purchases.");
            var chimera = catalog.Single(quality => quality.Element("name")!.Value == "Wildcard Chimera");
            var chimeraChoices = chimera.Element("bonus")!.Element("selectquality")!;
            Require(chimeraChoices.Elements("quality").Count() == 17
                && chimeraChoices.Element("discountqualities")!.Elements("quality").Count() == 13
                && chimera.Element("required")!.Descendants("quality").All(quality => quality.Value.StartsWith("Infected:", StringComparison.Ordinal))
                && Effect("Wildcard Chimera").Contains("Choose one optional infected power")
                && Effect("Wildcard Chimera").Contains("possible drawback discount")
                && Effect("Wildcard Chimera").Contains("zero listed cost does not mean free"),
                "Chimera help must distinguish one referenced power, optional drawbacks and unresolved final cost.");
            var technoshaman = catalog.Single(quality => quality.Element("name")!.Value == "Resonant Stream: Technoshaman");
            var cyberadept = catalog.Single(quality => quality.Element("name")!.Value == "Resonant Stream: Cyberadept");
            foreach (var stream in new[] { technoshaman, cyberadept })
                Require(stream.Element("required")!.Descendants("quality").Single().Value == "Technomancer"
                    && stream.Element("forbidden")!.Descendants("quality").Count() == 6,
                    "Stream explanations must retain the technomancer prerequisite and incompatible streams.");
            Require(technoshaman.Element("bonus")!.Elements().Single().Name.LocalName == "allowspritefettering"
                && Effect("Resonant Stream: Technoshaman").Contains("permanently fetter one sprite")
                && Effect("Resonant Stream: Technoshaman").Contains("grants no free sprite")
                && Effect("Resonant Stream: Technoshaman").Contains("Career cost equals its rating in Karma"),
                "Fettering permission must not be advertised as unlimited free sprites.");
            var cyberadeptSkills = cyberadept.Element("bonus")!.Elements("specificskill").ToArray();
            Require(cyberadept.Element("bonus")!.Element("cyberadeptdaemon") is not null
                && cyberadeptSkills.Length == 4 && cyberadeptSkills.All(skill => skill.Element("bonus")!.Value == "2")
                && cyberadeptSkills.Select(skill => skill.Element("condition")!.Value).Distinct().OrderBy(value => value)
                    .SequenceEqual(new[] { "Companion Sprite", "Fault Sprite" })
                && cyberadeptSkills.Select(skill => skill.Element("name")!.Value).Distinct().OrderBy(value => value)
                    .SequenceEqual(new[] { "Compiling", "Decompiling" })
                && Effect("Resonant Stream: Cyberadept").Contains("Fault or Companion")
                && Effect("Resonant Stream: Cyberadept").Contains("conditionally recover")
                && Effect("Resonant Stream: Cyberadept").Contains("not Essence or losses from bioware"),
                "Cyberadept help must preserve alternative sprite targets and conditional Resonance recovery.");
            string Describe(string bonus) => string.Join(" ", CreationQualityInfo.Effects("<quality><bonus>" + bonus + "</bonus></quality>"));
            foreach (string effect in new[] { "<allowspritefettering />", "<cyberadeptdaemon />",
                "<prototypetranshuman>9</prototypetranshuman>", "<selectquality><quality>Unknown</quality></selectquality>" })
                Require(Describe(effect) == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                    "Curated source-identity summaries must not invent mechanics for an unrelated or unknown source.");
            foreach (string name in new[] { "Animal Empathy", "City Slicker", "Outdoorsman", "Sense of Direction",
                "Vehicle Empathy", "Water Sprite", "Computer Illiterate", "Loss of Confidence", "Nasty Vibe",
                "Grease Monkey", "Alibi", "Closer", "Innocuous", "Memory Palace", "Hi-Rez",
                "Tough as Nails (Physical)", "Tough as Nails (Stun)", "Reduced Sense (Smell)",
                "Reduced Sense (Taste)", "Reduced Sense (Touch)", "Reduced Sense (Hearing)",
                "Reduced Sense (Sight)", "Reduced Sense (Astral Sight)" })
            {
                var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                string summary = CreationFlowStrings.Get("Qualities.Summary." + quality.Element("id")!.Value, "");
                Require(summary.Length > 40 && Effect(name).StartsWith(summary, StringComparison.Ordinal),
                    "A supported skill or monitor effect needs its own source-bound explanation: " + name);
            }
            Require(Effect("City Slicker").Contains("other than Survival")
                && Effect("City Slicker").Contains("general one-die penalty")
                && Effect("City Slicker").Contains("Outside urban areas, Perception loses one die")
                && Effect("Outdoorsman").Contains("without stacking terrain bonuses"),
                "Environmental bonuses must retain their exceptions and penalties, not stack alternative conditions.");
            Require(Effect("Vehicle Empathy").Contains("except Gunnery")
                && Effect("Water Sprite").Contains("two dice to Diving tests and two dice to Swimming tests")
                && Effect("Loss of Confidence").Contains("rating of at least 4")
                && Effect("Loss of Confidence").Contains("specialization bonuses do not apply")
                && Effect("Computer Illiterate").Contains("without lowering skill ratings"),
                "Skill help must preserve exclusions, selection restrictions and dice versus learned ratings.");
            Require(Effect("Alibi").Contains("plausible-seeming evidence")
                && Effect("Closer").Contains("life or death") && Effect("Innocuous").Contains("hiding in a crowd")
                && Effect("Hi-Rez").Contains("Other Computer tests do not gain this bonus"),
                "Conditional skill bonuses must not be advertised as unconditional.");
            Require(Effect("Tough as Nails (Physical)").StartsWith("Each level adds one box to your Physical condition monitor.", StringComparison.Ordinal)
                && Effect("Tough as Nails (Stun)").StartsWith("Each level adds one box to your Stun condition monitor.", StringComparison.Ordinal)
                && Effect("Reduced Sense (Sight)").Contains("rely on sight")
                && Effect("Reduced Sense (Astral Sight)").Contains("Assensing tests"),
                "Physical versus Stun boxes and visual versus astral perception must stay distinct.");
            Require(Describe("<selectskill limittoskill='Hacking' />") == CreationFlowStrings.Get("Qualities.Info.Manual", "")
                && Describe("<selectskill><val> </val></selectskill>") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "A skill-selection prompt with no modifier must not pass for a rule explanation.");
            Require(Effect("Aptitude").Contains("Maximum change: 1")
                && Describe("<selectskill><disablespecializationeffects /></selectskill>").Contains("Specialization bonuses do not apply"),
                "Maximum-only and specialization-only changes are real effects even without bonus dice.");
            Require(Effect("Bad Luck").Contains("Notoriety: 1")
                && Effect("Bad Luck").Contains("Spending Edge can backfire")
                && Effect("Bad Luck").Contains("only once per session")
                && Effect("Bad Luck").Contains("still consuming it")
                && !Effect("Bad Luck").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Bad Luck must explain its limited Edge reversal as well as its reputation effect.");
            Require(Describe("<notoriety>1</notoriety>").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))
                && Describe("<publicawareness>2</publicawareness>").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))
                && Describe("<astralreputation>1</astralreputation><selectskill />").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Reputation side effects must remain visible but must not masquerade as the full quality rules.");
            Require(Effect("Astral Beacon").Contains("last longer")
                && Effect("Astral Beacon").Contains("easier to read with Assensing")
                && Effect("Astral Beacon").Contains("without changing the observer's dice pool")
                && Effect("Distinctive Style").Contains("easier to remember, identify and track")
                && Effect("Distinctive Style").Contains("Astral searches are unaffected"),
                "Astral and physical identifiability must keep their scopes and thresholds distinct from dice modifiers.");
            Require(Effect("Combat Paralysis").Contains("half your opening Initiative score")
                && Effect("Combat Paralysis").Contains("later Initiative rolls are normal")
                && Effect("Combat Paralysis").Contains("Surprise and composure")
                && Effect("Combat Paralysis").Contains("under fire also suffer"),
                "Combat Paralysis must retain opening-score, Surprise and Composure effects without halving all later Initiative.");
            Require(Effect("Insomnia (Basic)").Contains("can slow Stun recovery")
                && Effect("Insomnia (Full)").Contains("Failed rest blocks Stun recovery")
                && new[] { "Insomnia (Basic)", "Insomnia (Full)" }.All(name =>
                    Effect(name).Contains("Edge refresh") && Effect(name).Contains("normal recovery"))
                && Effect("Insomnia (Basic)") != Effect("Insomnia (Full)"),
                "The two sleep-related drawbacks must distinguish slower recovery from a failed recovery attempt.");
            Require(Effect("Codeblock").Contains("one chosen, realistically used Matrix action that requires a test")
                && Effect("Codeblock").Contains("other Matrix actions are unaffected")
                && Effect("Simsense Vertigo").Contains("smartlinks, simrigs and image links")
                && Effect("Low Pain Tolerance").Contains("Physical and Stun injuries impose wound penalties sooner")
                && Effect("Low Pain Tolerance").Contains("without reducing either condition monitor")
                && Effect("Low Pain Tolerance").Contains("Wound-penalty interval change: -1"),
                "Interface penalties and earlier wound penalties must not become universal test penalties or smaller monitors.");
            Require(Effect("Elf Poser").Contains("Humans can pose as elves")
                && Effect("Elf Poser").Contains("without changing metatype or attributes")
                && Effect("Ork Poser").Contains("human or elf")
                && Effect("Ork Poser").Contains("does not change your metatype or attributes")
                && Effect("Spirit Bane").Contains("One chosen spirit type")
                && Effect("Spirit Bane").Contains("it resists your banishing more strongly")
                && Effect("Spirit Bane").Contains("Summoning and binding it become harder"),
                "Imitating a metatype must not grant attributes; spirit hostility must retain type scope and who rolls the bonus.");
            foreach (string frequency in new[] { "Uncommon", "Common" })
            {
                string mild = Effect($"Allergy ({frequency}, Mild)");
                string moderate = Effect($"Allergy ({frequency}, Moderate)");
                string severe = Effect($"Allergy ({frequency}, Severe)");
                string extreme = Effect($"Allergy ({frequency}, Extreme)");
                Require(mild.Contains("-2 dice on Physical tests")
                    && mild.Contains("-1 to resist attacks using it")
                    && moderate.Contains("-4 dice on Physical tests")
                    && moderate.Contains("-2 to resist attacks using it"),
                    "Mild/moderate allergies affect Physical tests and retain separate allergen-attack resistance penalties.");
                Require(severe.Contains("-4 dice on all tests")
                    && severe.Contains("ongoing unresisted Physical damage")
                    && severe.Contains("-3 to resist allergen attacks")
                    && extreme.Contains("actions -6 dice")
                    && extreme.Contains("faster unresisted Physical damage")
                    && extreme.Contains("resistance to allergen attacks -4")
                    && extreme.Contains("First Aid, Medicine or magic can stop shock"),
                    "Higher allergy summaries retain all-test scope, ongoing versus faster damage and shock treatment, not a full damage procedure.");
                Require(!mild.Contains("unresisted") && !moderate.Contains("unresisted")
                    && mild.Contains("no ongoing damage") && moderate.Contains("no ongoing damage")
                    && !severe.Contains("faster"),
                    "Lower allergy grades must not borrow ongoing damage or the extreme grade's faster damage.");
            }
            Require(Effect("Addiction (Mild)").Contains("Monthly cravings")
                && Effect("Addiction (Mild)").Contains("unresisted withdrawal: mental-based tests -2 dice for psychological dependence")
                && Effect("Addiction (Moderate)").Contains("Fortnightly cravings")
                && Effect("Addiction (Moderate)").Contains("unresisted withdrawal: mental-based tests -4 dice for psychological dependence")
                && new[] { "Mild", "Moderate" }.All(grade =>
                    Effect($"Addiction ({grade})").Contains("No persistent social penalty")),
                "Mild/moderate help preserves frequency, resisted-withdrawal distinction and dependence scope without doses or full procedures.");
            Require(Effect("Addiction (Severe)").Contains("Weekly cravings")
                && Effect("Addiction (Severe)").Contains("Social tests always -2")
                && Effect("Addiction (Burnout)").Contains("Daily cravings")
                && Effect("Addiction (Burnout)").Contains("mental-based tests -6 dice")
                && Effect("Addiction (Burnout)").Contains("Social tests always -3"),
                "Severe/burnout addiction help must distinguish withdrawal penalties from the persistent social penalty.");
            foreach (string frequency in new[] { "Common", "Specific" })
            {
                foreach (var (degree, dice) in new[] { ("Biased", 2), ("Outspoken", 4), ("Radical", 6) })
                {
                    string prejudice = Effect($"Prejudiced ({frequency}, {degree})");
                    Require(prejudice.Contains($"social tests with its members -{dice} dice")
                        && prejudice.Contains($"their Negotiation against you +{dice}")
                        && prejudice.Contains("Other groups are unaffected"),
                        "Prejudice severity must change both opponents' rolls only when interacting with the chosen group.");
                    Require(prejudice.Contains(frequency == "Common" ? "common group" : "narrowly defined group"),
                        "Target prevalence must remain distinct from the severity's dice modifiers.");
                }
            }
            Require(Effect("SINner (National)").Contains("15% gross-income tax")
                && Effect("SINner (National)").Contains("identity and biometrics")
                && Effect("SINner (National)").Contains("false identities do not erase the record")
                && Effect("SINner (Criminal)").Contains("15% gross-income tax")
                && Effect("SINner (Criminal)").Contains("replaces your former identity")
                && Effect("SINner (Criminal)").Contains("police scrutiny")
                && Effect("SINner (Criminal)").Contains("Notoriety: 1"),
                "National/criminal SINs need their registry, replacement and oversight consequences beyond the encoded reputation modifier.");
            Require(Effect("SINner (Corporate Limited)").Contains("20% gross-income tax")
                && Effect("SINner (Corporate Limited)").Contains("without leadership privileges")
                && Effect("SINner (Corporate Limited)").Contains("extraction risk")
                && Effect("SINner (Corporate)").Contains("10% gross-income tax")
                && Effect("SINner (Corporate)").Contains("corporate records")
                && Effect("SINner (Corporate)").Contains("no free corporate resources"),
                "Corporate SIN variants must not swap tax rates, disclose the same registry detail or imply free corporate equipment.");
            Require(!Describe("<notoriety>1</notoriety><memory>1</memory>").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "A reputation modifier alongside a described primary effect is not a reputation-only explanation.");
            string gremlins = Effect("Gremlins");
            Require(gremlins.Contains("Once, at the first level only: Notoriety: 1")
                && gremlins.Contains("glitches more easily with each level") && gremlins.Contains("Implants are unaffected")
                && gremlins.Contains("cannot sabotage others")
                && !gremlins.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Gremlins needs its glitch rule and scope, not just a one-time reputation modifier.");
            string ratedGremlins = string.Join(" ", CreationQualityInfo.Effects(
                catalog.Single(quality => quality.Element("name")!.Value == "Gremlins").ToString(), 3));
            Require(ratedGremlins == gremlins,
                "The per-level glitch description must not multiply the one-time Notoriety effect.");
            var unknownGremlins = new System.Xml.Linq.XElement(catalog.Single(quality => quality.Element("name")!.Value == "Gremlins"));
            unknownGremlins.Element("id")!.Value = Guid.Empty.ToString("D");
            Require(CreationQualityInfo.Effects(unknownGremlins.ToString()).Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "An unrelated first-level-only definition still needs the incomplete-description warning.");
            foreach (string name in new[] { "Ambidextrous", "Astral Chameleon", "Blandness", "Focused Concentration",
                "Gearhead", "Guts", "Human-Looking", "Juryrigger", "Natural Hardening", "Gremlins",
                "Mentor Spirit", "Paragon", "Inherent Program" })
            {
                var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                Require(summary.Length > 40 && Effect(name).StartsWith(summary, StringComparison.Ordinal),
                    "Core quality help must provide original source-bound prose: " + name);
            }
            Require(Effect("Astral Chameleon").Contains("twice as quickly")
                && Effect("Astral Chameleon").Contains("not your physical presence")
                && Effect("Blandness").Contains("Magical and Matrix searches are unaffected")
                && Effect("Blandness").Contains("standing out removes the benefit"),
                "Concealment explanations must retain their different scopes and exceptions.");
            Require(Effect("Focused Concentration").Contains("one spell or complex form")
                && Effect("Focused Concentration").Contains("up to this quality's rating")
                && Effect("Focused Concentration").Contains("Drain and Fading still apply")
                && Effect("Guts").Contains("resisting fear or intimidation")
                && Effect("Guts").Contains("does not make you immune"),
                "Mental-discipline help must not grant unlimited sustaining, fear immunity or attack bonuses.");
            Require(Effect("Gearhead").Contains("20% more Speed or +1 Handling")
                && Effect("Gearhead").Contains("Extending the boost damages the vehicle")
                && Effect("Juryrigger").Contains("temporary improvised repairs")
                && Effect("Juryrigger").Contains("burns out critical components"),
                "Technical tricks must retain their alternatives, temporary duration and damage risk.");
            Require(Effect("Human-Looking").Contains("actual metatype and its attributes do not change")
                && Effect("Natural Hardening").Contains("one point of natural biofeedback filtering")
                && Effect("Natural Hardening").Contains("not ordinary physical attacks"),
                "Appearance and biofeedback protection must not imply altered metatype or general armor.");
            foreach (string name in new[] { "Mentor Spirit", "Paragon", "Inherent Program" })
                Require(Effect(name).Contains("not yet explained here")
                    && Effect(name).Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "A guide/program choice is useful partial help, not the selected profile's full mechanics.");
            string infirm = Effect("Infirm");
            Require(Regex.Matches(infirm, "Once, at the first level only: Augmentations cannot raise this attribute above its natural maximum").Count == 4
                && Regex.Matches(infirm, "Maximum change: -1").Count == 4,
                "Infirm needs all four natural-maximum clamps as well as its four per-level maximum reductions.");
            string profile = "<naturalweapon><name>Test claw</name><reach>0</reach><damage>({STR}+1)P</damage><ap>-1</ap><useskill>Unarmed Combat</useskill><accuracy>Physical</accuracy><source>HIDDEN_BOOK</source><page>999</page></naturalweapon>";
            string DescribeSource(string body) => string.Join(" ", CreationQualityInfo.Effects("<quality>" + body + "</quality>"));
            var unknownRated = CreationQualityInfo.Effects("<quality><bonus><futureeffect>3</futureeffect></bonus></quality>", 3);
            Require(unknownRated.Count == 1 && unknownRated[0] == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "A rating notice must not disguise missing rule descriptions as usable help.");
            string formulaRated = string.Join(" ", CreationQualityInfo.Effects(
                "<quality><bonus><spellresistance>Rating * 2</spellresistance></bonus></quality>", 3));
            Require(formulaRated.Contains("Spell resistance: Rating * 2") && !formulaRated.Contains("Spell resistance: 6"),
                "The help renderer must never evaluate rating expressions or invent a combined total.");
            string scoped = DescribeSource("<bonus><notoriety>1</notoriety></bonus><firstlevelbonus><notoriety>1</notoriety></firstlevelbonus>");
            Require(Regex.Matches(scoped, "Notoriety: 1").Count == 2
                && Regex.Matches(scoped, "Once, at the first level only").Count == 1,
                "Equal normal and first-level values have different scopes and must not be deduplicated together.");
            string weapons = DescribeSource("<naturalweapons>" + profile + profile + "</naturalweapons>");
            Require(Regex.Matches(weapons, "Natural weapon: Test claw").Count == 2
                && weapons.Contains("Damage formula: (Strength+1) Physical damage")
                && weapons.Contains("Armor penetration: -1") && weapons.Contains("Accuracy limit: Physical")
                && !weapons.Contains("HIDDEN_BOOK") && !weapons.Contains("999")
                && !weapons.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Natural weapons retain multiplicity and literal formulas but never source/page referrals.");
            Require(Describe(profile).Contains("Natural weapon: Test claw"),
                "A natural weapon can also be encoded inside the normal bonus node.");
            foreach (string partial in new[] {
                "<naturalweapons condition='unknown'>" + profile + "</naturalweapons>",
                "<naturalweapons>" + profile.Replace("</naturalweapon>", "<futurecondition>unknown</futurecondition></naturalweapon>") + "</naturalweapons>",
                "<naturalweapons>" + profile.Replace("<ap>-1</ap>", "") + "</naturalweapons>",
                "<naturalweapons>" + profile.Replace("<ap>-1</ap>", "<ap>-1</ap><ap>-2</ap>") + "</naturalweapons>",
                "<naturalweapons>" + profile.Replace("<ap>-1</ap>", "<ap condition='unknown'>-1</ap>") + "</naturalweapons>",
                "<firstlevelbonus condition='unknown'><notoriety>1</notoriety></firstlevelbonus>" })
                Require(DescribeSource(partial).Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "Unknown wrapper/weapon conditions and absent or ambiguous fields must remain visibly partial.");
            Require(DescribeSource("<naturalweapons><armor>2</armor></naturalweapons>") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "Only weapon definitions may be interpreted in the naturalweapons wrapper.");
            string reference = DescribeSource("<addweapon rating='2'>Named weapon</addweapon><addweapon>Named weapon</addweapon>");
            Require(Regex.Matches(reference, "Granted weapon; attack details not yet available: Named weapon").Count == 2
                && reference.Contains("Rating: 2") && reference.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))
                && !reference.Contains("Damage formula"), "Weapon references are not full attack profiles or ambient-catalog authority.");
            Require(DescribeSource("<addweapon>00000000-0000-0000-0000-000000000001</addweapon>") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "Unresolved weapon identities must not leak into help.");
            Require(Effect("Crystalline Shards").Contains("Armor penetration: 4")
                && Effect("Crystalline Shards").Contains("Skill: Throwing Weapons")
                && Effect("Crystalline Shards").Contains("positive armor modifier helps the target")
                && !Effect("Crystalline Shards").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "The shards' positive armor modifier must not be inverted or confused with a damage bonus.");
            Require(Effect("Crystalline Blade").Contains("Reach: 1") && Effect("Crystalline Blade").Contains("Armor penetration: -2")
                && Effect("Crystalline Claws").Contains("Damage formula: (Strength+1) Physical damage"),
                "The distinct crystal weapon profiles must not borrow one another's damage, reach or armor values.");
            string dealer = Effect("Dealer Connection");
            Require(dealer.Contains("Choose one vehicle category for a 10% purchase discount")
                && dealer.Contains("Choose from: Drones") && dealer.Contains("Choose from: Groundcraft")
                && dealer.Contains("Choose from: Watercraft") && dealer.Contains("Choose from: Aircraft")
                && !dealer.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Dealer Connection must distinguish one selected discount category from all vehicle categories.");
            foreach (var (name, lifestyle) in new[] { ("Trust Fund I", "Medium"), ("Trust Fund II", "Low"),
                ("Trust Fund III", "High"), ("Trust Fund IV", "Medium") })
            {
                string trust = Effect(name);
                Require(trust.Contains("Lifestyle eligible for trust-fund support: " + lifestyle)
                    && trust.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "Trust Fund levels identify lifestyle eligibility, not cash amounts or complete income rules.");
            }
            string redliner = Effect("Redliner");
            Require(redliner.Contains("at most +2: Agility") && redliner.Contains("at most +2: Strength")
                && redliner.Contains("lose 3 boxes per eligible cyberlimb pair, at most 6")
                && !redliner.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Redliner's cyberlimb-dependent bonuses must include the Physical monitor penalty and both caps.");
            Require(Effect("Cyber-Singularity Seeker").Contains("at most +2: Willpower")
                && !Effect("Cyber-Singularity Seeker").Contains("lose 3 boxes"),
                "Cyber-Singularity Seeker must not borrow Redliner's monitor penalty.");
            Require(Effect("Overclocker").Contains("Add 1 to one chosen overclocked Matrix attribute")
                && !Effect("Overclocker").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "An empty overclocker flag should explain its chosen-attribute benefit.");
            Require(Effect("Friends in High Places").Contains("Connection 8 or higher")
                && Effect("Friends in High Places").Contains("four times your Charisma"),
                "High-Connection contact points need their threshold and separate Charisma-based budget.");
            Require(Describe("<actiondicepool category='Matrix' />") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "An action-selection prompt alone must not invent Codeslinger's benefit without its definition-bound summary.");
            foreach (string malformed in new[] { "<trustfund>5</trustfund>", "<trustfund>Rating</trustfund>",
                "<cyberseeker>unknown</cyberseeker>", "<overclocker>unknown</overclocker>",
                "<dealerconnection />", "<dealerconnection><category>Unknown</category></dealerconnection>",
                "<dealerconnection><category>00000000-0000-0000-0000-000000000001</category></dealerconnection>" })
                Require(Describe(malformed) == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                    "Unknown enum values and absent/unresolved choice targets must not imply a known benefit.");
            Require(Describe("<dealerconnection><category>Drones</category><futurecondition>unknown</futurecondition></dealerconnection>")
                .Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "A known discount must not swallow an unknown dealer restriction.");
            Require(Describe("<overclocker futurecondition='unknown' />")
                .Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Unknown conditions on a known flag must remain visible as incomplete.");
            foreach (string conditional in new[] { "<overclocker condition='Only at night' />",
                "<cyberseeker condition='Only at night'>BOX</cyberseeker>", "<trustfund condition='Only at night'>1</trustfund>",
                "<dealerconnection condition='Only at night'><category>Drones</category></dealerconnection>" })
                Require(Describe(conditional).Contains("When: Only at night"),
                    "Known conditions on special benefits must be displayed, not merely accepted by validation.");
            string datahaven = Effect("Prime Datahaven Membership");
            Require(datahaven.Contains("Granted contact") && datahaven.Contains("Connection: 5")
                && datahaven.Contains("Base Loyalty: 1") && datahaven.Contains("Fixed Loyalty: 3")
                && datahaven.Contains("Group contact") && datahaven.Contains("Does not cost contact points"),
                "Granted group contact must preserve free cost, Connection and fixed versus base Loyalty.");
            string practice = Effect("Practice, Practice, Practice");
            Require(practice.Contains("Weapon Accuracy change, not bonus dice") && practice.Contains("Modifier: 1")
                && practice.Contains("Chosen skill · Except: Combat skills"),
                "Weapon Accuracy is not an attack-pool bonus; preserve the accepted skill exclusion.");
            string deathDealer = Effect("Death Dealer (Adept)");
            Require(deathDealer.Contains("Weapon damage change, not bonus dice") && deathDealer.Contains("Bonus: 1")
                && deathDealer.Contains("Choose from: Astral Combat,Blades,Clubs,Exotic Melee Weapon,Unarmed Combat")
                && deathDealer.Contains("Spell Drain change"), "Weapon damage's skill restriction and increased spell Drain must remain visible.");
            string chainBreaker = Effect("Chain Breaker");
            Require(Regex.Matches(chainBreaker, "Choose an additional summonable spirit type, not a summoned spirit").Count == 2
                && chainBreaker.Contains("Unavailable skill: Binding"), "Two spirit-type choices must not be deduplicated or described as summoned allies.");
            string conjurer = Effect("Dedicated Conjurer");
            Require(conjurer.Contains("Based on skill: Summoning")
                && conjurer.Contains("Number of choices: Base skill rating / 2 (Rounded down)")
                && conjurer.Contains("Unavailable skill: Spellcasting") && !conjurer.Contains("addtoselected"),
                "Dedicated Conjurer needs its full-increment skill basis without exposing bookkeeping XML.");
            string hedge = Effect("Hedge Witch/Wizard");
            Require(hedge.Contains("Spell choices restricted to one chosen category · Except: Rituals")
                && hedge.Contains("Additionally permitted spell category: Rituals"),
                "Excluding Rituals from the category choice must not hide that Rituals remain separately permitted.");
            Require(Effect("Elementalist (Fire)").Contains("Summoning restricted to one chosen spirit type · Choose from: Spirit of Fire")
                && Effect("Elementalist (Fire)").Contains("Unavailable skill group: Enchanting"),
                "Elementalist must show its spirit restriction and forbidden skill group.");
            Require(Describe("<selectcontact />") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "A contact selection prompt alone does not explain Sensei or another quality's rules.");
            string contact = Describe("<selectcontact><type>nongroup</type><forcedloyalty>Rating + 1</forcedloyalty><free /></selectcontact>");
            Require(contact.Contains("Chosen existing contact") && contact.Contains("Fixed Loyalty: Rating + 1")
                && contact.Contains("Does not cost contact points") && !contact.Contains("Granted contact"),
                "Changing an existing contact is not creating a new one; retain symbolic values.");
            Require(Describe("<addcontact />").Contains("Connection: 1") && Describe("<addcontact />").Contains("Base Loyalty: 1"),
                "Default contact values must not disappear when omitted from source XML.");
            string repeatedContacts = Describe("<addcontact /><addcontact />");
            Require(Regex.Matches(repeatedContacts, "Granted contact").Count == 2,
                "Two independent granted contacts must remain two displayed contacts.");
            string unknownWeaponCondition = Describe("<weaponskillaccuracy><value>1</value><selectskill limittoskill='Pistols' futurecondition='unknown' /></weaponskillaccuracy>");
            Require(unknownWeaponCondition.Contains("Choose from: Pistols")
                && unknownWeaponCondition.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Unknown nested weapon-choice conditions must not be silently dropped.");
            Require(Describe("<weaponskillaccuracy><selectskill /></weaponskillaccuracy>") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "A missing weapon modifier must not be guessed as a +1 bonus.");
            Require(Describe("<addspirit skill='Summoning' ratingdivisor='unknown' />") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "An unknown spirit-choice divisor must not be evaluated or invented.");
            string symbolicSpirit = Describe("<addspirit skill='Summoning' ratingdivisor='2'><futurecondition>unknown</futurecondition></addspirit>");
            Require(symbolicSpirit.Contains("Base skill rating / 2 (Rounded down)")
                && symbolicSpirit.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Known spirit-choice basis must not conceal an unknown condition.");
            string powers = Describe("<critterpowers><power rating='2'>Armor</power><power select='Fire'>Immunity</power></critterpowers>");
            Require(powers.Contains("Granted power: Armor · Rating: 2") && powers.Contains("Fixed detail: Fire"),
                "Granted power ratings and fixed selections cannot be dropped.");
            string optional = Describe("<optionalpowers count='2'><optionalpower>Armor</optionalpower><optionalpower>Fear</optionalpower></optionalpowers>");
            Require(optional.Contains("Number of choices: 2") && !optional.Contains("Granted power:"),
                "Optional power count is a choice count, not a grant of every listed power.");
            string gearPrice = Describe("<addgear><name>Test item</name><rating>Rating + 1</rating><quantity>2</quantity><fullcost /></addgear>");
            Require(gearPrice.Contains("Pay full price") && gearPrice.Contains("Quantity: 2")
                && gearPrice.Contains("Rating: Rating + 1") && !gearPrice.Contains("No nuyen cost"),
                "Full-cost grants must not be described as free; retain quantity and symbolic rating.");
            string hiddenGearCondition = Describe("<addgear><name>Test item</name><children><child><name>Child</name><futurecondition>unknown</futurecondition></child></children></addgear>");
            Require(hiddenGearCondition.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Unknown conditions on included equipment must keep the whole explanation partial.");
            string deeperGear = Describe("<addgear><name>Parent</name><children><child><name>Child</name><children><child><name>Not an admitted grandchild</name></child></children></child></children></addgear>");
            Require(!deeperGear.Contains("Not an admitted grandchild")
                && deeperGear.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Display must not grant recursively nested equipment beyond Core's immediate-child contract.");
            string unresolved = Describe("<addqualities><addquality>00000000-0000-0000-0000-000000000001</addquality></addqualities>");
            Require(!unresolved.Contains("00000000") && unresolved.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "An unresolved quality reference stays hidden and partial, not apparently complete.");
            string specialization = Describe("<selectexpertise limittoskill='Artisan' limittospecialization='Painting,Sculpture' />");
            Require(specialization.Contains("Choose from: Artisan") && specialization.Contains("Choose specialization from: Painting,Sculpture"),
                "Expertise must preserve both skill and specialization restrictions.");
            string sprint = Describe("<movementreplace><category>Fly</category><speed>sprint</speed><val>500</val></movementreplace>");
            Require(sprint.Contains("Replacement sprint distance") && sprint.Contains("Category: Flying")
                && sprint.Contains("Meters per hit: 5") && !sprint.Contains("500"), "Replacement sprint distance is also stored in hundredths.");
            string noCategory = Describe("<movementreplace><val>3</val></movementreplace>");
            Require(noCategory.Contains("Replacement walking multiplier") && noCategory.Contains("All movement types"),
                "A replacement without explicit speed/category defaults to walking for all movement types.");
            string mixedUnits = Describe("<sprintbonus><category>Ground</category><val>50</val><percent>25</percent></sprintbonus>");
            Require(mixedUnits.Contains("Meters per hit: 0.5") && mixedUnits.Contains("Percentage change: 25"),
                "Only the sprint-distance value uses hundredths; percentage changes must remain unchanged.");
            string symbolic = Describe("<sprintbonus><category>Ground</category><val>Rating * 100</val></sprintbonus>");
            Require(symbolic.Contains("Meters per hit: (Rating * 100) / 100"),
                "Symbolic unit conversion must remain an unevaluated expression.");
            string crystal = Describe("<essencepenaltyt100>-150</essencepenaltyt100><essencepenaltymagonlyt100>150</essencepenaltymagonlyt100>");
            Require(crystal.Contains("Essence change: -1.5") && crystal.Contains("Essence adjustment for Magic loss only: 1.5"),
                "Magic-specific Essence adjustment is not a grant of Magic attribute points.");
            string legacyInitiative = Describe("<initiativepass precedence='0'>1</initiativepass>");
            Require(legacyInitiative.Contains("Initiative dice: 1")
                && legacyInitiative.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Legacy initiative tag means dice, but unexplained stacking precedence remains partial.");
            Require(Describe("<movementreplace><speed>unknown</speed><val>500</val></movementreplace>")
                == CreationFlowStrings.Get("Qualities.Info.Manual", ""), "Unknown movement types must not be guessed as walking.");
            string fading = Describe("<fadingvalue specific='Resonance Spike'>-2</fadingvalue>");
            Require(fading.Contains("Fading value change: -2") && fading.Contains("Only for: Resonance Spike"),
                "A targeted Fading modifier must not appear to apply to every complex form.");
            string limit = Describe("<limitmodifier><limit>Mental</limit><value>1</value><condition>LimitCondition_SkillsKnowledgeAcademic</condition></limitmodifier>");
            Require(limit.Contains("Limit: Mental") && limit.Contains("Modifier: 1")
                && limit.Contains("Academic Knowledge") && !limit.Contains("LimitCondition_"),
                "Limit values and translated conditions were omitted.");
            string restricted = Describe("<selectskill minimumrating='4' limittoskill='Hacking'><val>-2</val><disablespecializationeffects /></selectskill>");
            Require(restricted.Contains("Minimum skill rating: 4") && restricted.Contains("Choose from: Hacking")
                && restricted.Contains("Specialization bonuses do not apply"), "Skill-choice restrictions were lost.");
            string partialText = Describe("<specificskill><name>Perception</name><bonus>1</bonus><futurecondition>unknown</futurecondition></specificskill>");
            Require(partialText.Contains("Bonus: 1") && partialText.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "An unknown field must produce a partial-description warning instead of disappearing silently.");
            string duplicatePartial = Describe("<specificskill><name>Perception</name><bonus>1</bonus></specificskill>"
                + "<specificskill><name>Perception</name><bonus>1</bonus><futurecondition>unknown</futurecondition></specificskill>");
            Require(duplicatePartial.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Text deduplication must not discard an unknown condition on the second effect.");
            string conditionalLifestyle = Describe("<lifestylecost lifestyle='Low' condition='once'>10</lifestylecost>");
            Require(conditionalLifestyle.Contains("Lifestyle: Low") && conditionalLifestyle.Contains("When: One-time cost"),
                "Lifestyle scope and one-time conditions must remain visible.");
            string expression = string.Join(" ", CreationQualityInfo.Effects(
                "<quality><bonus><specificskill><name>Test skill</name><bonus>Rating + 1</bonus><condition>Only at night</condition></specificskill></bonus></quality>"));
            Require(expression.Contains("Rating + 1") && expression.Contains("Only at night"),
                "Read-only help must preserve expressions and conditions without evaluating rules.");
            string unknown = string.Join(" ", CreationQualityInfo.Effects(
                "<quality><name>Catlike</name><id>00000000-0000-0000-0000-000000000001</id><bonus><unrecognized /></bonus></quality>"));
            Require(unknown == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "An unknown source identity must not borrow another quality's prose from its display name.");
            try
            {
                CreationQualityInfo.Effects("<!DOCTYPE quality [<!ENTITY x 'unsafe'>]><quality>&x;</quality>");
                throw new InvalidOperationException("Quality help accepted a DTD.");
            }
            catch (System.Xml.XmlException) { }
        }
        finally { CultureInfo.CurrentUICulture = oldCulture; }
    }

    private sealed class CountedQualityCatalog(IReadOnlyList<CharacterCreationQualityCatalogOption> values)
        : IReadOnlyList<CharacterCreationQualityCatalogOption>
    {
        public int Reads { get; set; }
        public int Count => values.Count;
        public CharacterCreationQualityCatalogOption this[int index] { get { Reads++; return values[index]; } }
        public IEnumerator<CharacterCreationQualityCatalogOption> GetEnumerator()
        {
            foreach (var value in values) { Reads++; yield return value; }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void VerifyQualitiesSinglePassBinding(CharacterCreationQualitiesState state, CharacterOverviewState overview)
    {
        var options = state.Authority.Options.ToList();
        var counted = new CountedQualityCatalog(options);
        var observed = state with { Authority = state.Authority with { Options = counted } };
        Require(CreationQualitiesPhoneAuthority.IsReady(observed, overview), "Counting changed the exact Core catalog.");
        int singleValidationReads = counted.Reads;
        var draft = new CreationQualitiesPhoneDraft();
        Require(draft.Bind(observed, overview), "Initial exact binding failed.");
        var choice = observed.Authority.Options.First(option => option.IsSelectable && option.EligibilityIsExact
            && string.IsNullOrWhiteSpace(option.DisableReasonKey));
        string[] selected = [choice.OptionId];
        var preview = CharacterCreationQualitiesRules.Evaluate(new(observed.Binding, observed.Authority, selected));
        Require(draft.TryAdopt(observed, overview, new(preview.CanConfirm ? CharacterCreationFoundationOutcomes.Success
                : CharacterCreationFoundationOutcomes.Blocked, preview, preview.Blockers), selected)
            && draft.Bind(observed, overview) && draft.SelectedOptionIds.SequenceEqual(selected)
            && draft.Preview?.PreviewDigest == preview.PreviewDigest,
            "Rebinding the same exact display discarded its unsaved selection.");
        var nextBinding = observed.Binding with { CreationKarmaUsedBeforeQualities = observed.Binding.CreationKarmaUsedBeforeQualities + 1 };
        var next = observed with { Binding = nextBinding,
            Preview = CharacterCreationQualitiesRules.Evaluate(new(nextBinding, observed.Authority, [])) };
        next = next with { SnapshotDigest = CharacterCreationQualitiesRules.ComputeStateDigest(next) };
        counted.Reads = 0;
        Require(draft.Bind(next, overview), "Changed exact binding failed.");
        int rebindReads = counted.Reads;
        Require(rebindReads == singleValidationReads,
            $"Binding a changed exact display must validate its catalog once, not repeatedly: {rebindReads} vs {singleValidationReads} option reads.");
        Require(draft.Matches(next, overview) && draft.Preview?.PreviewDigest == next.Preview.PreviewDigest
            && draft.SelectedOptionIds.Count == 0,
            "Rebinding did not adopt the new exact Core preview.");

        // IReadOnlyList is not necessarily immutable. The same record/digest
        // must never serve as a cached admission after its backing list changes.
        var originalOption = options[0];
        options[0] = originalOption with { KarmaCost = originalOption.KarmaCost + 1 };
        Require(!draft.Bind(next, overview) && draft.Preview is null && draft.SelectedOptionIds.Count == 0 && !draft.Matches(next, overview),
            "A changed backing catalog retained its prior binding or preview.");
        options[0] = originalOption;
        Require(draft.Bind(next, overview) && draft.Matches(next, overview), "Restoring the exact input did not allow fresh validation.");
        Require(!draft.Bind(next with { Binding = next.Binding with { ContentRevision = overview.ContentRevision + 1 } }, overview)
            && draft.Preview is null && draft.SelectedOptionIds.Count == 0,
            "A stale overview retained a previous quality preview.");
        Console.WriteLine($"PASS single-pass quality binding ({singleValidationReads} catalog reads), changed budget, mutable catalog and stale revision rejected");
    }

    internal static async Task RunCreationQualityDetailsAsync(string contentRoot, string? smokeWorkspacePath = null)
    {
        VerifyQualitySummaryContent(contentRoot);
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true);
            var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeQualities: true);
            if (smokeWorkspacePath is not null)
            {
                Require(Path.IsPathFullyQualified(smokeWorkspacePath), "Use an explicit private smoke fixture path.");
                // Preserve unmodified real Core output for the isolated AVD's
                // affected-route smoke, not a claim of UI-driven earlier steps.
                File.Copy(Path.Combine(runtime.StateDirectory, "workspaces", runtime.Id.Value + ".json"),
                    smokeWorkspacePath, overwrite: false);
                Console.WriteLine("QUALITY_SMOKE_WORKSPACE " + runtime.Id.Value);
            }
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            var coordinator = runtime.Coordinator;
            var original = coordinator.State;
            var loaded = await coordinator.LoadCreationQualitiesForDisplayAsync(original, default);
            var state = loaded.Value ?? throw new InvalidOperationException("SETUP: quality authority missing.");
            VerifyQualitiesSinglePassBinding(state, original);
            var catalogPage = new CreationQualitiesPage(coordinator);
            await MinimalPrepareAsync(catalogPage);
            var catalogNavigation = new NavigationPage(catalogPage);
            SearchBar Search() => MinimalVisible(catalogPage).OfType<SearchBar>().Single();
            var retainedSearch = Search();
            var retainedReview = MinimalVisible(catalogPage).OfType<Button>().Single(item =>
                item.AutomationId == "creation-qualities-open-review");
            var detachedHelp = MinimalVisible(catalogPage).OfType<Button>().First(item =>
                item.AutomationId?.StartsWith("creation-quality-info-", StringComparison.Ordinal) == true);
            retainedSearch.Text = "no-such-quality-focus-regression";
            ((ISearchBarController)retainedSearch).OnSearchButtonPressed();
            Require(ReferenceEquals(Search(), retainedSearch) && ReferenceEquals(retainedReview,
                MinimalVisible(catalogPage).OfType<Button>().Single(item => item.AutomationId == "creation-qualities-open-review")),
                "Submitting a Quality search must not detach its editor or rebuild unrelated actions.");
            Require(!MinimalVisible(catalogPage).OfType<Button>().Any(item =>
                item.AutomationId?.StartsWith("creation-quality-info-", StringComparison.Ordinal) == true),
                "An empty search retained old help actions.");
            ((IButtonController)detachedHelp).SendClicked();
            Require(ReferenceEquals(catalogNavigation.CurrentPage, catalogPage), "Filtered-out help navigated from a stale row.");
            retainedSearch.Text = string.Empty;
            Require(ReferenceEquals(Search(), retainedSearch) && MinimalVisible(catalogPage).OfType<Button>().Any(item =>
                item.AutomationId?.StartsWith("creation-quality-info-", StringComparison.Ordinal) == true),
                "Clearing a search detached the native editor instead of refreshing only results.");
            var nextPage = MinimalVisible(catalogPage).OfType<Button>().Single(item =>
                item.AutomationId == "creation-qualities-catalog-next");
            Require(nextPage.IsEnabled, "SETUP: expected multiple available catalog pages.");
            ((IButtonController)nextPage).SendClicked();
            string secondPage = MinimalVisibleText(catalogPage);
            ((IButtonController)nextPage).SendClicked();
            Require(ReferenceEquals(Search(), retainedSearch) && MinimalVisibleText(catalogPage) == secondPage,
                "Paging detached the editor or a detached pager changed the new result list.");
            await VerifyQualityHelpScrollReturnAsync(ui, catalogPage, catalogNavigation);
            retainedSearch = Search();
            retainedSearch.Text = "Ambidextrous";
            ((ISearchBarController)retainedSearch).OnSearchButtonPressed();
            Require(ReferenceEquals(Search(), retainedSearch)
                && MinimalVisibleText(catalogPage).Contains("Ambidextrous")
                && ReferenceEquals(catalogNavigation.CurrentPage, catalogPage),
                "Replacing and submitting a Quality search must stay in the catalog, not open Review.");
            MinimalRender(catalogPage);
            string freshCatalog = MinimalVisibleText(catalogPage);
            retainedSearch.Text = string.Empty;
            ((ISearchBarController)retainedSearch).OnSearchButtonPressed();
            Require(!ReferenceEquals(Search(), retainedSearch) && MinimalVisibleText(catalogPage) == freshCatalog,
                "A detached SearchBar changed the new catalog.");
            RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
            var editor = CreationQualitiesPhoneAuthority.ProjectEditor(state, original);
            var draft = new CreationQualitiesPhoneDraft();
            draft.Bind(state, original);
            var option = draft.AvailableOptions(state, original, editor, default)
                .Where(item => !item.IsMetagenic)
                .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.FollowUpChoiceLabel)).First();
            var configure = new CreationQualityConfigurePage(coordinator, state, editor, option, draft, original);
            var previewResult = coordinator.PreviewCreationQualities(state.Binding, [option.OptionId], original);
            Require(draft.TryAdopt(state, original, previewResult, [option.OptionId])
                && previewResult.Value is { CanConfirm: true }, "SETUP: selected quality must have a real confirmable preview.");
            var preview = previewResult.Value!;
            var checkpoint = CharacterCreationQualitiesCheckpoint.CreateReviewed(preview, [option.OptionId], Guid.NewGuid());
            var store = CharacterCreationQualitiesCheckpointStore.CreateDefault(original.DisplayOwnerContext,
                coordinator.IsCreationQualitiesOwnerCurrent);
            Require(store.TryCreate(checkpoint, out checkpoint, out string blocker), blocker);
            var review = new CreationQualitiesReviewPage(coordinator, checkpoint, store, original);
            var oldCulture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    VerifyQualityDisclosure(configure, "creation-quality-configure-technical-details",
                        "creation-quality-configure-toggle", option.OptionId, option.SourceId.ToString("D"));
                    string configureText = MinimalVisibleText(configure);
                    Require(configureText.Contains(QualityCatalogStrings.Name(option.SourceId, option.Name)) && configureText.Contains(CreationQualitiesPage.Signed(option.KarmaCost))
                        && (string.IsNullOrWhiteSpace(option.FollowUpChoiceLabel) || configureText.Contains(option.FollowUpChoiceLabel)),
                        "Configure lost the quality name, exact cost or readable follow-up.");
                    var configuredSource = state.Authority.Options.Single(item => item.OptionId == option.OptionId);
                    string inlineExplanation = CreationQualityInfo.Effects(configuredSource.SourceNodeXml, option.Rating)[0];
                    Require(configureText.Contains(inlineExplanation, StringComparison.Ordinal),
                        "Configure must show the same source-bound explanation as ! without another page.");
                    VerifyQualityDisclosure(review, "creation-qualities-review-technical-details",
                        "creation-qualities-confirm-draft", checkpoint.TransactionId.ToString("D"), preview.PreviewDigest,
                        preview.AuthorityDigest, preview.Binding.RawCharacterXmlDigest, preview.Binding.AuxiliaryStateDigest,
                        option.OptionId, option.SourceId.ToString("D"));
                    Require(MinimalVisibleText(review).Contains(QualityCatalogStrings.Name(option.SourceId, option.Name))
                        && MinimalVisible(review).OfType<Button>().Single(button => button.AutomationId == "creation-qualities-confirm-draft").Text
                            == CreationFlowStrings.Get("Qualities.Review.Confirm", "missing"),
                        "Review lost the selected name or localized save action.");
                    var emptyPreview = coordinator.PreviewCreationQualities(state.Binding, [], original).Value!;
                    var empty = new CreationQualitiesReviewPage(coordinator,
                        CharacterCreationQualitiesCheckpoint.CreateReviewed(emptyPreview, [], Guid.NewGuid()), store, original);
                    MinimalRender(empty);
                    Require(MinimalVisibleText(empty).Contains(CreationFlowStrings.Get("Qualities.Review.Empty", "missing"))
                        && MinimalVisible(empty).OfType<Button>().Single(button => button.AutomationId == "creation-qualities-confirm-draft").IsEnabled,
                        "An empty valid review must explain that no additional qualities are selected.");
                    MinimalRequireNoMachineValues(empty);
                    foreach (string name in new[] { "Analytical Mind", "Catlike", "Unsteady Hands", "Aptitude", "Erased", "Animal Empathy" }
                        .Concat(SourceEffectQualitySummaries.Select(rule => rule.Name))
                        .Concat(ConciseQualitySummaries.Select(rule => rule.Name))
                        .Concat(FreeInsectSpiritSpecies.Select(species => "Free Insect Spirit: " + species)))
                    {
                        var helpOption = state.Authority.Options.First(item => item.Name == name);
                        var help = new CreationQualityInfoPage(coordinator, original, helpOption);
                        string helpText = MinimalVisibleText(help);
                        string expected = CreationFlowStrings.Get("Qualities.Summary." + helpOption.SourceId.ToString("D"), "");
                        // A concise explanation need not be padded to forty characters.
                        // The real-catalog check above enforces the upper editorial bound.
                        Require(expected.Length > 0 && helpText.Contains(expected)
                            && !helpText.Contains("Rulebook", StringComparison.OrdinalIgnoreCase)
                            && !helpText.Contains("Regelbuch", StringComparison.OrdinalIgnoreCase)
                            && !helpText.Contains("página", StringComparison.OrdinalIgnoreCase),
                            "Quality help must contain the localized inline explanation, not a book citation.");
                        MinimalRequireNoMachineValues(help);
                        VerifyQualityHelpEffects(help, helpOption);
                    }
                    // Display-only malformed/custom definitions must keep their
                    // caveats visible even when the numeric detail list is folded.
                    var artisanOption = state.Authority.Options.First(item => item.Name == "The Artisan's Way");
                    var altered = System.Xml.Linq.XElement.Parse(artisanOption.SourceNodeXml);
                    altered.SetElementValue("karma", "999");
                    altered.Element("bonus")!.Add(new System.Xml.Linq.XElement("unknown-test-effect"));
                    var changedOption = artisanOption with { SourceNodeXml = altered.ToString() };
                    var changedHelp = new CreationQualityInfoPage(coordinator, original, changedOption);
                    VerifyQualityHelpEffects(changedHelp, changedOption);
                    Require(MinimalVisibleText(changedHelp).Contains(CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", ""))
                        && MinimalVisibleText(changedHelp).Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                        "Changed/partial definition warnings must stay visible with details collapsed.");
                    var missingHelp = new CreationQualityInfoPage(coordinator, original,
                        artisanOption with { SourceNodeXml = "<quality><name>Unexplained fixture</name></quality>" });
                    Require(MinimalVisibleText(missingHelp).Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                        && !MinimalVisible(missingHelp).OfType<Button>().Any(button => button.AutomationId == "creation-quality-info-effects-toggle"),
                        "Missing help must be honest and must not offer an empty disclosure.");
                    var ratedOption = state.Authority.Options.First(item => item.Name == "Will to Live" && item.Rating == 3);
                    string levelNotice = CreationFlowStrings.Format("Qualities.Info.BaseEffects", "missing", ratedOption.Rating);
                    var ratedHelp = new CreationQualityInfoPage(coordinator, original, ratedOption);
                    Require(MinimalVisibleText(ratedHelp).Contains(levelNotice),
                        "The catalog help page must pass the accepted option's level to the description.");
                    VerifyQualityHelpEffects(ratedHelp, ratedOption);
                    // Presentation-only grant fixture: no grant is persisted or
                    // admitted. Both help constructors must preserve the level.
                    var grant = new CharacterCreationGrantedQuality("help-grant-fixture", ratedOption.SourceId,
                        "help-selection-fixture", ratedOption.Name, ratedOption.Type, ratedOption.Rating, 0,
                        false, false, false, "Earlier choice", [], "help-digest-fixture");
                    var grantedHelp = new CreationQualityInfoPage(coordinator, original, grant, ratedOption.SourceNodeXml);
                    Require(MinimalVisibleText(grantedHelp).Contains(levelNotice),
                        "Already-granted qualities must retain the same base-versus-selected-level distinction.");
                    var firstLevelHelp = new CreationQualityInfoPage(coordinator, original, ratedOption with { Rating = 1 });
                    Require(!MinimalVisibleText(firstLevelHelp).Contains(CreationFlowStrings.Format("Qualities.Info.BaseEffects", "missing", 1)),
                        "A single-level description must not show an irrelevant combined-total notice.");
                }
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                Require(store.TryRead(out var unchanged, out blocker) && unchanged.CheckpointDigest == checkpoint.CheckpointDigest,
                    "Rendering/disclosure mutated the durable review.");

                var retainedHelp = new CreationQualityInfoPage(coordinator, original,
                    state.Authority.Options.First(item => item.Name == "The Artisan's Way"));
                var retainedHelpBody = (VerticalStackLayout)((ScrollView)retainedHelp.Content!).Content!;
                var retainedHelpDetails = retainedHelpBody.Children.OfType<VerticalStackLayout>().Single();
                var retainedHelpToggle = retainedHelpBody.Children.OfType<Button>().Single(button =>
                    button.AutomationId == "creation-quality-info-effects-toggle");

                Require(store.TryBeginApply(CharacterCreationQualitiesCheckpointCas.From(checkpoint), out var applying, out blocker), blocker);
                var result = await coordinator.ConfirmCreationQualitiesAsync(applying, display: original);
                Require(result is { Receipt: not null, MutationOutcomeKnown: true, Outcome: CreationQualitiesPhoneOutcomes.Applied },
                    "Actual quality confirmation failed.");
                Require(store.TryRecordApplied(CharacterCreationQualitiesCheckpointCas.From(applying), result.Receipt!, out var applied, out blocker), blocker);
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                Require(saved.ContentRevision == before.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision
                    && saved.Document.Content == before.Document.Content, "Quality confirmation must save one auxiliary revision, not live character effects.");
                await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                var cold = coordinator.LoadCreationQualities().Value!;
                Require(CreationQualitiesPhoneAuthority.ReceiptMatchesPersistedState(applying, result.Receipt!, cold),
                    "Exact quality selection/receipt did not survive cold-store reopen.");
                var receipt = new CreationQualitiesReceiptPage(coordinator, applied, result.Receipt!, store);
                foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    VerifyQualityDisclosure(receipt, "creation-qualities-receipt-technical-details",
                        "creation-qualities-receipt-acknowledge", result.Receipt!.TransactionId.ToString("D"),
                        result.Receipt.ReceiptDigest, result.Receipt.DraftDigest, result.Receipt.PlanDigest, result.Receipt.CommandDigest);
                    Require(MinimalVisibleText(receipt).Contains(CreationFlowStrings.Get("Qualities.Receipt.Safe", "missing"))
                        && MinimalVisible(receipt).OfType<Button>().Single(button => button.AutomationId == "creation-qualities-receipt-acknowledge").Text
                            == CreationFlowStrings.Get("Qualities.Receipt.Continue", "missing"),
                        "Saved quality confirmation lost localized continuation or pending-finalization guidance.");
                }
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                Require(store.TryRead(out var retained, out blocker) && retained.CheckpointDigest == applied.CheckpointDigest,
                    "Receipt disclosure acknowledged or changed the pending receipt.");
                Require(store.TryAcknowledgeApplied(CharacterCreationQualitiesCheckpointCas.From(applied), out blocker), blocker);
                var owner = owners.Current;
                var freshCatalogPage = new CreationQualitiesPage(coordinator);
                await MinimalPrepareAsync(freshCatalogPage);
                await VerifyQualityHelpScrollReturnAsync(ui, freshCatalogPage, new NavigationPage(freshCatalogPage), () =>
                {
                    owners.Set(ContactsOwnerB);
                    owners.Set(owner);
                });
                string staleCatalog = MinimalVisibleText(catalogPage);
                var staleSearch = Search();
                staleSearch.Text = "Catlike";
                ((ISearchBarController)staleSearch).OnSearchButtonPressed();
                Require(MinimalVisibleText(catalogPage) == staleCatalog,
                    "A search callback admitted catalog work across an owner A→B→A transition.");
                ((IButtonController)retainedHelpToggle).SendClicked();
                Require(!retainedHelpDetails.IsVisible, "An expired help disclosure reopened old effect data.");
                foreach (NativePageBase stale in new NativePageBase[] { configure, review, receipt, retainedHelp })
                {
                    MinimalRender(stale);
                    Require(!MinimalVisible(stale).OfType<Button>().Any(), "Stale owner generation still exposes quality actions or diagnostics.");
                    MinimalRequireNoMachineValues(stale);
                }
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
            }
            finally { CultureInfo.CurrentUICulture = oldCulture; }
            Console.WriteLine("PASS quality configure/review/receipt: EN/DE/ES, exact hidden diagnostics, stale controls, one save/cold reopen, no display mutations");
        });
    }

    private static async Task VerifyQualityHelpScrollReturnAsync(
        IssuedPageUiContext ui, CreationQualitiesPage page, NavigationPage navigation, Action? invalidateBeforeDispatch = null)
    {
        const double savedY = 735;
        var scroll = (ScrollView)page.Content!;
        var controller = (IScrollViewController)scroll;
        var observed = new List<double>();
        controller.ScrollToRequested += (_, request) =>
        {
            observed.Add(request.ScrollY);
            controller.SendScrollFinished();
        };
        string Range() => MinimalVisible(page).OfType<Label>().Single(item =>
            item.AutomationId == "creation-qualities-catalog-range").Text;
        string range = Range();
        controller.SetScrolledPosition(0, savedY);
        var helpButton = MinimalVisible(page).OfType<Button>().First(item =>
            item.AutomationId?.StartsWith("creation-quality-info-", StringComparison.Ordinal) == true);
        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)helpButton).SendClicked()));
        var help = navigation.CurrentPage;
        Require(help is CreationQualityInfoPage, "Help did not open from the current catalog.");
        await navigation.PopAsync();
        controller.SetScrolledPosition(0, 0);
        // Exercise the real fresh appearance load, not cached authority. Headless
        // MAUI has no native layout/attachment callbacks, supplied explicitly here.
        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
        ((IView)scroll).Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, 400, 600));
        typeof(ScrollView).GetProperty(nameof(ScrollView.ContentSize))!.SetValue(scroll,
            new Microsoft.Maui.Graphics.Size(400, 3000));
        typeof(CreationQualitiesPage).GetMethod("RestoreAfterHelp", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, [help]);
        invalidateBeforeDispatch?.Invoke();
        await ui.DrainDispatchedAsyncVoidAsync();
        if (invalidateBeforeDispatch is not null)
        {
            Require(observed.Count == 0, "A queued help restoration scrolled after an owner A→B→A transition.");
            IssuedPageLifecycle(page, "OnDisappearing");
            return;
        }
        Require(observed.SequenceEqual([savedY]) && Range() == range,
            "Returning from read-only help must restore the position and catalog page after fresh loading.");
        MinimalRender(page);
        await ui.DrainDispatchedAsyncVoidAsync();
        Require(observed.Count == 1, "A later catalog render replayed the help scroll restoration.");
        IssuedPageLifecycle(page, "OnDisappearing");
    }

    private static void VerifyQualityHelpEffects(CreationQualityInfoPage page, CharacterCreationQualityCatalogOption option)
    {
        string[] effects = CreationQualityInfo.Effects(option.SourceNodeXml, option.Rating).ToArray();
        string[] notices =
        [
            CreationFlowStrings.Get("Qualities.Info.Manual", ""),
            CreationFlowStrings.Get("Qualities.Info.Additional", ""),
            CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", ""),
            CreationFlowStrings.Format("Qualities.Info.BaseEffects", "", option.Rating)
        ];
        var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        var details = body.Children.OfType<VerticalStackLayout>().SingleOrDefault();
        var toggle = body.Children.OfType<Button>().SingleOrDefault(button =>
            button.AutomationId == "creation-quality-info-effects-toggle");
        string[] folded = effects.Skip(1).Where(line => !notices.Contains(line)).ToArray();
        string visible = MinimalVisibleText(page);
        Require(visible.Contains(QualityCatalogStrings.Name(option.SourceId, option.Name)) && visible.Contains(effects[0])
            && visible.Contains(CreationFlowStrings.Format("Qualities.Info.Cost", "", option.Rating,
                CreationQualitiesPage.Signed(option.KarmaCost)))
            && (string.IsNullOrWhiteSpace(option.FollowUpChoiceLabel) || visible.Contains(option.FollowUpChoiceLabel))
            && effects.Where(notices.Contains).All(visible.Contains),
            "Concise help hid the summary, exact cost, chosen follow-up or a completeness/level warning.");
        if (folded.Length == 0)
        {
            Require(details is null && toggle is null, "A prose-only explanation has an empty detail toggle.");
            return;
        }
        Require(details is { IsVisible: false } && toggle is not null
            && toggle.Text == CreationFlowStrings.Get("Qualities.Info.ShowEffects", "")
            && details.Children.OfType<Label>().Select(label => label.Text).SequenceEqual(folded),
            "Effect details must default to folded and retain every exact source-derived row in order.");
        Require(!MinimalVisible(page).Contains(details!), "Folded rules leaked into the accessible visual tree.");
        ((IButtonController)toggle!).SendClicked();
        Require(details!.IsVisible && effects.All(MinimalVisibleText(page).Contains)
            && toggle!.Text == CreationFlowStrings.Get("Qualities.Info.HideEffects", ""),
            "Opening details lost effects, caveats or localized disclosure state.");
        MinimalRequireNoMachineValues(page);
        ((IButtonController)toggle!).SendClicked();
        Require(!details.IsVisible && toggle!.Text == CreationFlowStrings.Get("Qualities.Info.ShowEffects", ""),
            "Closing details did not restore concise help.");
        MinimalRender(page);
        ((IButtonController)toggle!).SendClicked();
        Require(!details.IsVisible && !ReferenceEquals(toggle!.Parent, body)
            && !body.Children.OfType<VerticalStackLayout>().Single().IsVisible,
            "A detached callback reopened its old details or changed a refreshed explanation.");
    }

    private static void VerifyQualityDisclosure(NativePageBase page, string panelId, string actionId, params string[] exactValues)
    {
        MinimalRender(page);
        MinimalRequireNoMachineValues(page);
        Require(!MinimalVisibleText(page).Contains("CharacterDocumentChanged"), "Ordinary guidance leaks implementation jargon.");
        var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        var panel = body.Children.OfType<VerticalStackLayout>().Single(item => item.AutomationId == panelId);
        var toggle = panel.Children.OfType<Button>().Single();
        var action = MinimalVisible(page).OfType<Button>().Single(item => item.AutomationId == actionId);
        Require(action.IsEnabled && body.Children.IndexOf(action) < body.Children.IndexOf(panel)
            && toggle.Text == CreationFlowStrings.Get("Qualities.ShowDetails", "missing"),
            "Technical details displaced or disabled the primary action.");
        ((IButtonController)toggle).SendClicked();
        Require(exactValues.All(value => MinimalVisibleText(page).Contains(value, StringComparison.Ordinal))
            && toggle.Text == CreationFlowStrings.Get("Qualities.HideDetails", "missing"),
            "Expanded quality diagnostics lost exact values or localization.");
        ((IButtonController)toggle).SendClicked();
        MinimalRequireNoMachineValues(page);
        MinimalRender(page);
        ((IButtonController)toggle).SendClicked();
        MinimalRequireNoMachineValues(page);
        Require(!((VisualElement)panel.Children[1]).IsVisible,
            "A detached quality disclosure reopened its old data after refresh.");
    }

    internal static async Task RunMinimalUiAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (var (locale, saveGear, saveBudget) in new[]
            {
                ("en-GB", "Save equipment", "Save budget"),
                ("de-AT", "Ausrüstung speichern", "Budget speichern"),
                ("es-MX", "Guardar equipo", "Guardar presupuesto")
            })
            {
                var localized = AndroidSurfaceStrings.Resolve(locale);
                Require(localized["GearPreview.Confirm"] == saveGear
                    && localized["ResourcesPreview.Confirm"] == saveBudget,
                    "Creation confirmation must describe the action in the player's language.");
                foreach (var key in new[] { "Resources.CurrentBudget", "Resources.CoreAuthority",
                    "Gear.DraftBasket", "Gear.ActiveCatalog", "GearPreview.ExactProjection" })
                    Require(!Regex.IsMatch(localized[key], "Core|XML|authority|autoridad|Autorität",
                        RegexOptions.IgnoreCase), "Player-facing headings expose implementation jargon.");
            }
            const string id = "c49a893a-d445-4aac-bec0-c8501cba4c2c";
            var label = NativeTheme.Body(id);
            var details = NativeTheme.TechnicalDetails(label, "test-diagnostics");
            var root = new VerticalStackLayout { details };
            var toggle = details.Children.OfType<Button>().Single();
            Require(!label.IsVisible && !MinimalVisibleText(root).Contains(id),
                "Diagnostics are exposed by default.");
            ((IButtonController)toggle).SendClicked();
            Require(label.IsVisible && label.Text == id && MinimalVisibleText(root).Contains(id),
                "Explicit troubleshooting lost the exact value.");
            ((IButtonController)toggle).SendClicked();
            Require(!label.IsVisible, "Diagnostics cannot be collapsed.");
            root.Clear();
            ((IButtonController)toggle).SendClicked();
            Require(!label.IsVisible, "A detached disclosure reopened old diagnostics.");
            Require(NativeTheme.Body(id).Text == id && NativeTheme.BookProse(id).Text == id,
                "Minimalism must not rewrite arbitrary player text or prose.");
            Require(NativeTheme.Body("Readable").FontSize >= 15
                && NativeTheme.BookProse("Chapter").FontSize >= 18
                && NativeTheme.PrimaryButton("Continue").HeightRequest >= 48,
                "Simplification shrank readable text or touch targets.");
            var overlay = NativeAuthoritySemantics.Overlay(NativeTheme.Body("Saved"),
                NativeAuthoritySemantics.Identifier("test-machine-id", id));
            Require(!MinimalVisibleText(overlay).Contains(id),
                "Ordinary-build TalkBack exposes invisible machine values.");

            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true, creationPrerequisite: true);
            var before = PrepareActualFinalizationReadyContext(runtime);
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            AssertCreationReadinessCopy(runtime.Coordinator);
            var currentCulture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (var (locale, words) in new[] { ("en-GB", "saved Attributes"),
                             ("de-AT", "gespeicherten Attribute"), ("es-MX", "Atributos guardados") })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    const string locked = "creation-prerequisite-dependent-attributes-draft-exists";
                    string message = CreationFlowStrings.DashboardBlocker(locked);
                    Require(message.Contains(words) && message == CreationFlowStrings.Get("Dashboard.MethodLocked", "missing"),
                        "Locked method lacks localized dependent-Attributes guidance.");
                    Require(CreationFlowStrings.DashboardBlocker("fixture-unknown-9d74b5ce")
                        == CreationFlowStrings.Get("Dashboard.StepBlocked", "missing"),
                        "Unknown codes must stay in diagnostics rather than leak into ordinary guidance.");
                }
            }
            finally { CultureInfo.CurrentUICulture = currentCulture; }
            var lockedMethod = await runtime.Coordinator.LoadCreationPrerequisiteAsync();
            Require(lockedMethod.Blockers.Concat(lockedMethod.Value?.Blockers ?? []).Contains(
                "creation-prerequisite-dependent-attributes-draft-exists"),
                "SETUP: saved Attributes must actually lock prerequisite edits.");
            var dashboard = new BuildPage(runtime.Coordinator);
            var snapshot = runtime.Coordinator.State.CreationWizard!;
            Require(CreationDashboardProjectionBinding.TryCreate(runtime.Coordinator.State, snapshot, out var binding),
                "SETUP: current dashboard binding missing.");
            var projection = CreationDashboardAuthorityProjection.Loading(binding!) with
            {
                Prerequisite = lockedMethod,
                Progress = CreationDashboardAuthorityPhaseProgress.ForBuildMethod(snapshot.BuildMethod) with
                { Prerequisite = CreationDashboardAuthorityPhaseState.Ready }
            };
            var methodRoute = (CreationBudgetRoute)typeof(BuildPage).GetMethod("AddCreationMethodRoute",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dashboard, [snapshot, projection, lockedMethod])!;
            Require(!methodRoute.CanOpen && methodRoute.Detail == CreationFlowStrings.DashboardBlocker(
                "creation-prerequisite-dependent-attributes-draft-exists")
                && methodRoute.Blockers.SequenceEqual(["creation-prerequisite-dependent-attributes-draft-exists"])
                && !MinimalVisible(dashboard).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-stage-method").IsEnabled,
                "Plain locked-method guidance changed readiness or gave a misleading Karma reason.");
            var header = new VerticalStackLayout();
            bool hasPicker = (bool)typeof(BuildPage).GetMethod("AddWorkspacePicker",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dashboard, [header])!;
            Require(hasPicker == (runtime.Coordinator.State.OpenWorkspaces.Count > 1)
                && (!hasPicker || header.Children.OfType<Picker>().Single().AutomationId == "build-workspace-picker"),
                "Dashboard heading must retain the workspace selector only when needed.");
            var actual = new CharacterCreationGearInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>());
            // Priority editing is intentionally locked once dependent stages
            // exist. Use a real new runner, not the ready-for-Gear fixture.
            await using var priorityRuntime = new NativeRewardRuntime(contentRoot,
                linkedOwners: owners, creationPrerequisite: true, creationAttributes: true,
                productionCreationOverview: true);
            await priorityRuntime.Coordinator.InitializeAsync();
            await AccountStartupTask(priorityRuntime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            var prioritySeed = PreparePrerequisiteOwnerFixture(priorityRuntime);
            await HydrateFinalizationOwnerAsync(priorityRuntime, owners, prioritySeed);
            var prerequisite = (await priorityRuntime.Coordinator.LoadCreationPrerequisiteAsync()).Value
                ?? throw new InvalidOperationException("SETUP: real prerequisite authority unavailable.");
            var (assignments, selections) = PrerequisiteSelections(prerequisite);
            var assignmentsPreview = (await priorityRuntime.Coordinator.PreviewCreationPrerequisiteAsync(
                prerequisite.Binding, assignments, selections)).Value!;
            var assignmentReview = new CreationPrerequisitePreviewPage(priorityRuntime.Coordinator,
                assignmentsPreview, assignments, selections, CharacterCreationBuildMethods.Priority);
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(assignmentReview, "OnAppearing")));
            MinimalRequireNoMachineValues(assignmentReview);
            MinimalRequireFreshDisclosure(assignmentReview);
            Require(MinimalVisibleText(assignmentReview).Contains(assignmentsPreview.TalentSelection!.Name)
                && MinimalVisible(assignmentReview).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-prerequisite-confirm").IsEnabled,
                "Readable assignments lost the actual Talent or exact confirmation.");
            ((IButtonController)MinimalVisible(assignmentReview).OfType<Button>().Single(x =>
                x.AutomationId == "creation-prerequisite-preview-details-toggle")).SendClicked();
            Require(MinimalVisibleText(assignmentReview).Contains(assignmentsPreview.PreviewDigest),
                "Review diagnostics must retain the exact preview digest.");
            IssuedPageLifecycle(assignmentReview, "OnDisappearing");
            var priorities = new CreationPrerequisitePage(priorityRuntime.Coordinator, prerequisite);
            MinimalRender(priorities);
            MinimalRequireNoMachineValues(priorities);
            Require(MinimalVisible(priorities).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-prerequisite-prepare-preview"),
                "Minimal Priorities hid its review action.");
            var draft = new CreationPrerequisitePhoneDraft();
            draft.Bind(prerequisite, priorityRuntime.Coordinator.State);
            var category = new CreationPriorityCategoryPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Attributes);
            MinimalRender(category);
            MinimalRequireNoMachineValues(category);
            Require(MinimalVisible(category).OfType<Button>().Count(x =>
                    x.AutomationId?.StartsWith("creation-prerequisite-rank-") == true) == 5,
                "Minimal rank list removed choices or their unavailability explanations.");
            Require(draft.TrySelect(prerequisite, priorityRuntime.Coordinator.State,
                CharacterCreationPriorityCategoryIds.Heritage, "A"), "SETUP: Heritage rank unavailable.");
            var heritage = new CreationPriorityDetailPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Heritage);
            MinimalRender(heritage);
            MinimalRequireNoMachineValues(heritage);
            Require(MinimalVisible(heritage).OfType<Button>().Any(x =>
                x.AutomationId?.StartsWith("creation-prerequisite-heritage-option-") == true),
                "Minimal Heritage list has no actual choices.");
            Require(draft.TrySelect(prerequisite, priorityRuntime.Coordinator.State,
                CharacterCreationPriorityCategoryIds.Talent, "B"), "SETUP: Talent rank unavailable.");
            var talent = new CreationPriorityDetailPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Talent);
            MinimalRender(talent);
            MinimalRequireNoMachineValues(talent);
            Require(MinimalVisible(talent).OfType<Button>().Any(x =>
                x.AutomationId?.StartsWith("creation-prerequisite-talent-option-") == true),
                "Minimal Talent list has no actual choices.");
            foreach (var grantedTalent in draft.TalentOptions(prerequisite, priorityRuntime.Coordinator.State)
                .Where(option => option.IsEnabled && option.Blockers.Count == 0
                    && (option.ActiveSkillGrant is not null || option.SkillGroupGrant is not null)
                    && CreationPrerequisitePhoneAuthority.IsTalentGrantAuthoritySupported(option)))
            {
                Require(draft.TrySelectTalent(prerequisite, priorityRuntime.Coordinator.State, grantedTalent.SelectionId),
                    "SETUP: supported granted-skill Talent cannot be selected.");
                var grantPage = new CreationTalentSkillGrantPage(priorityRuntime.Coordinator, draft, prerequisite, grantedTalent.SelectionId);
                MinimalRender(grantPage);
                MinimalRequireNoMachineValues(grantPage);
                MinimalRequireFreshDisclosure(grantPage);
                Require(MinimalVisibleText(grantPage).Contains(grantedTalent.Name)
                    && MinimalVisibleText(grantPage).Contains("Granted rating"),
                    "Talent skill choices lost the readable Talent or actual granted rating.");
                ((IButtonController)MinimalVisible(grantPage).OfType<Button>().Single(button =>
                    button.AutomationId == "creation-prerequisite-talent-grant-details-toggle")).SendClicked();
                Require(MinimalVisibleText(grantPage).Contains(grantedTalent.ActiveSkillGrant?.GrantDigest
                    ?? grantedTalent.SkillGroupGrant!.GrantDigest), "Talent diagnostics lost the exact grant digest.");
            }
            var confirmedAssignments = await priorityRuntime.Coordinator.ConfirmCreationPrerequisiteAsync(
                assignmentsPreview, assignments, selections);
            Require(confirmedAssignments.Outcome == CharacterCreationFoundationOutcomes.Success,
                "SETUP: prerequisite confirmation failed.");
            var attributesAuthority = priorityRuntime.Coordinator.LoadCreationAttributes().Value
                ?? throw new InvalidOperationException("SETUP: Attribute authority unavailable.");
            Require(CreationAttributesPhoneAuthority.IsReady(
                attributesAuthority, priorityRuntime.Coordinator.State), "SETUP: editable Attribute authority unavailable.");
            var attributesPage = new CreationAttributesPage(priorityRuntime.Coordinator, attributesAuthority);
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(attributesPage, "OnAppearing")));
            var scroll = (ScrollView)attributesPage.Content!;
            Element? scrollTarget = null;
            ((IScrollViewController)scroll).ScrollToRequested += (_, request) =>
            {
                scrollTarget = request.Element;
                ((IScrollViewController)scroll).SendScrollFinished();
            };
            var jump = MinimalVisible(attributesPage).OfType<Button>().Single(x =>
                x.AutomationId == "creation-attributes-budget-normal-jump");
            ((IButtonController)jump).SendClicked();
            Require(scrollTarget?.AutomationId == "creation-attributes-normal-heading",
                "Normal points must jump to the actual normal Attribute list.");
            foreach (var attribute in attributesAuthority.Attributes)
            {
                var value = MinimalVisible(attributesPage).OfType<Label>().Single(x =>
                    x.AutomationId == "creation-attributes-open-" + CreationAttributesPage.Token(attribute.AttributeId) + "-value");
                Require(value.Text == attribute.Current.ToString(CultureInfo.InvariantCulture)
                    && value.FontAttributes.HasFlag(FontAttributes.Bold) && value.FontSize >= 24
                    && value.TextColor == NativeTheme.Text, "Current Attribute rating is not prominent/readable.");
            }
            scrollTarget = null;
            MinimalRender(attributesPage);
            ((IButtonController)jump).SendClicked();
            Require(scrollTarget is null, "A detached budget control still scrolls a refreshed screen.");
            MinimalRequireNoMachineValues(attributesPage);
            MinimalRequireFreshDisclosure(attributesPage);
            IssuedPageLifecycle(attributesPage, "OnDisappearing");
            var gear = new CreationGearPage(runtime.Coordinator, actual, runtime.Presenter);
            await MinimalPrepareAsync(gear);
            var visible = MinimalVisibleText(gear);
            MinimalRequireNoMachineValues(gear);
            Require(MinimalVisible(gear).OfType<Button>().Any(x => x.AutomationId == "creation-gear-preview")
                && MinimalVisible(gear).OfType<SearchBar>().Any()
                && visible.Contains("¥"), "Minimal Gear hid budget, search or review.");
            var copy = AndroidSurfaceStrings.Resolve();
            var unchanged = MinimalVisible(gear).OfType<Label>()
                .Single(x => x.AutomationId == "creation-gear-preview-authority");
            Require(unchanged.Text == copy["Gear.ChangeBasket"]
                && unchanged.TextColor.Equals(NativeTheme.Muted)
                && !MinimalVisible(gear).OfType<Button>()
                    .Single(x => x.AutomationId == "creation-gear-preview").IsEnabled,
                "An unchanged saved basket is neutral guidance, not an error or a new save.");
            var disclosure = MinimalVisible(gear).OfType<Button>()
                .Single(x => x.AutomationId == "creation-gear-details-toggle");
            ((IButtonController)disclosure).SendClicked();
            Require(MinimalVisible(gear).OfType<Label>().Any(x =>
                    x.AutomationId == "creation-gear-binding-snapshot-digest"),
                "Gear diagnostic anchors were deleted instead of disclosed.");
            // A refresh must not retain an expanded old snapshot.
            MinimalRender(gear);
            MinimalRequireNoMachineValues(gear);

            var original = runtime.Coordinator.State;
            var prepared = actual.Prepare(original, [new("gear:" + id, 1)]).PreparedPreview
                ?? throw new InvalidOperationException("SETUP: actual Gear preview unavailable.");
            var preview = new CreationGearPreviewPage(runtime.Coordinator, actual, runtime.Presenter,
                prepared, AndroidSurfaceStrings.Resolve(), original);
            await MinimalPrepareAsync(preview);
            MinimalRequireNoMachineValues(preview);
            Require(MinimalVisible(preview).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-gear-confirm" && x.IsEnabled
                    && x.Text == copy["GearPreview.Confirm"]),
                "Minimal preview hid or disabled explicit confirmation.");

            var resources = new CharacterCreationResourcesInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>());
            var resourcePage = new CreationResourcesPage(runtime.Coordinator, resources, runtime.Presenter, actual);
            await MinimalPrepareAsync(resourcePage);
            MinimalRequireNoMachineValues(resourcePage);
            var resourceText = MinimalVisibleText(resourcePage);
            Require(resourceText.Contains("¥")
                && !resourceText.Contains(copy["Common.DraftRevision"])
                && !resourceText.Contains(copy["Resources.ExactBudget"])
                && MinimalVisible(resourcePage).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-resources-open-gear" && x.IsEnabled),
                "Resources must retain budget and next action without repeating technical status.");
            // Exercise the warning branch without modifying the workspace or
            // pretending a fabricated budget is admissible for a save.
            var budget = resources.Load(original).State!.Budget;
            typeof(CreationResourcesPage).GetMethod("AddBudget", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(resourcePage, [budget with { IsExact = false }, "Warning test", "test-incomplete-budget"]);
            Require(MinimalVisible(resourcePage).OfType<Label>().Any(x =>
                x.Text == copy["Resources.IncompleteBudget"] && x.TextColor.Equals(NativeTheme.Danger)),
                "Minimal Resources suppressed an incomplete-cost warning.");
            Require(FinalizationDocumentDigest(new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!)
                    == FinalizationDocumentDigest(before),
                "Rendering/disclosing minimal UI mutated the workspace.");
        });
        Console.WriteLine("PASS minimal native UI: localized plain actions, collapsed diagnostics, neutral unchanged basket, retained budget/warnings/confirm, unchanged prose and persistence");
    }

    private static async Task MinimalPrepareAsync(NativePageBase page)
    {
        await (Task)page.GetType().GetMethod("PrepareForAppearanceRefreshAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [CancellationToken.None])!;
        MinimalRender(page);
    }

    private static void MinimalRequireFreshDisclosure(NativePageBase page)
    {
        var field = page.GetType().GetField("_technicalDetails", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previous = (View)field.GetValue(page)!;
        var parent = previous.Parent;
        MinimalRender(page);
        var current = (View)field.GetValue(page)!;
        Require(!ReferenceEquals(previous, current) && parent is not null
            && ReferenceEquals(previous.Parent, parent) && current.Parent is not null
            && !ReferenceEquals(current.Parent, parent),
            "A refreshed disclosure reused a native child still owned by its previous container.");
    }

    private static void MinimalRender(NativePageBase page) => page.GetType()
        .GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);

    private static IEnumerable<VisualElement> MinimalVisible(IVisualTreeElement element)
    {
        if (element is VisualElement { IsVisible: false }) yield break;
        if (element is VisualElement view) yield return view;
        foreach (var child in element.GetVisualChildren())
            foreach (var descendant in MinimalVisible(child))
                yield return descendant;
    }

    private static string MinimalVisibleText(IVisualTreeElement element) => string.Join("\n",
        MinimalVisible(element).SelectMany(view => new[]
        {
            view is Label label ? label.Text : view is Button button ? button.Text : string.Empty,
            SemanticProperties.GetDescription(view)
        }));

    private static void MinimalRequireNoMachineValues(IVisualTreeElement element)
    {
        Require(!Regex.IsMatch(MinimalVisibleText(element),
            @"sha256:|\b(?:[0-9a-f]{32}|[0-9a-f]{64})\b|\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b",
            RegexOptions.IgnoreCase), "Normal UI/accessibility contains machine identifiers.");
    }
}
