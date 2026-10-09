# Quarterly planning verification profile

Local composed fixture API and real Web, production services and SQLite read/write behavior; SQL Server checks use disposable databases on `localhost\SQLEXPRESS`. Fiscal calendar is AccountingConfiguration, company timezone is the native workspace profile. Fiscal-year label is the year in which the fiscal year starts; quarter boundaries are half-open and converted independently through the company timezone.

Test users are active Owner and a Sales Manager. The Owner holds company-performance oversight and departmental responsibilities. The Manager initially lacks company planning responsibility; granting company-performance responsibility still grants no access to the Owner's private monthly snapshots. No provider, mailbox delivery, payment or publication is invoked.

Browser records identities freshly from `/_uat/p25/profile`. Never reuse historical fixture IDs or PIDs as live instructions. Desktop is 1440×1000; narrow is 390×844. In-app automation initialization failed at the Windows deny-read ACL helper. The documented safe substitute is a fresh disposable headless Edge session running the actual Web and composed API.
