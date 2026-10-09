# 10 — What needs contracts, permits or external APIs (nothing was bought or activated)

| Item | Why it cannot be done locally | Possible future cost (not incurred) |
|---|---|---|
| Real delivery of manufacturer reports | needs an agreement and a technical channel with each manufacturer (or the national pharmacovigilance gateway) and a legal basis for the transfer | integration time; possibly a gateway fee; legal review |
| National id / insurance member id linkage, prescription registry, national drug codes | needs access granted by the competent authority/insurer; codes must come from the official source, never invented | usually a contract + audit; fees vary |
| Barcode/QR scanning | engineering only for the camera part; **verification against official product registries** needs an official data source | camera permission work; registry access |
| Real identity provider (OIDC/SMS/e-ID) | needs a provider account | per-user or per-SMS cost |
| LLM for guidance wording / assistant | needs an approved provider, a key and a data-processing agreement; patient context may leave the system only with the patient's consent | pay-per-token, or a self-hosted GPU |
| Production hosting, TLS, backups, monitoring | needs servers/cloud and a domain (explicitly **not** bought) | VPS or cloud + backups |
| Push notifications for messages | needs APNs/FCM setup | free tiers exist; Apple developer account is paid |
| Store releases (Play, App Store, Windows/macOS signing) | developer accounts and signing certificates | Apple/Google fees, certificates |
| Clinical validation of guidance templates | needs clinical reviewers; the shipped text is a safe-tone prototype | reviewer time |
| Penetration test, DPIA/privacy review, regulatory classification | needs qualified people | consulting |
