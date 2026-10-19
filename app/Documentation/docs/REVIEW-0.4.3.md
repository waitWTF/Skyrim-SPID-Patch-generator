# 0.4.3 development preview — uniformed-NPC protection

- Guards, soldiers, officers, commanders, housecarls/bodyguards, Thalmor, Penitus Oculatus, Dawnguard and Vigilants are excluded from all NPC audiences, using effective identity and inherited class/faction/keyword/outfit evidence.
- The Guards group is removed. Broad Warriors/All NPCs groups and restored saved choices resolve against the protected roster.
- Mixed template alternatives are excluded if any alternative is protected. Redguard race names, Imperial civilian races and ordinary civilian crime factions are not protection evidence.
- Generation from the wizard requires a scanned NPC roster; old broad unresolved selections cannot bypass protection.
- Full integration checks, including eligibility regressions, passed. No in-game validation was performed.

## Updating existing output
Rescan with 0.4.3 and regenerate every existing outfit entry that should use the protected roster. Untouched entries retained by incremental patch merging keep their previous recipient lists. Review My patch to ensure all intended entries were updated. This change does not automatically inspect male/female armor mesh compatibility.

## Classification limit
Protection uses record identity and inherited evidence rather than runtime uniform inspection. Unusually named or script-controlled mod NPCs may evade classification, and broad protection terms may exclude additional NPCs. Review audiences and report misclassifications.
