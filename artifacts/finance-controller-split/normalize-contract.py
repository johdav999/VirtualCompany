import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[2]
out = root / 'artifacts/finance-controller-split'
routes = json.loads((out / 'routes-before.json').read_text(encoding='utf-8'))
for route in routes:
    route['ActionAttributes'] = '|'.join(a for a in route['ActionAttributes'].split('|') if not a.startswith('[System.Runtime.CompilerServices.'))
    route['Parameters'] = '|'.join(re.sub(r'\[System\.Runtime\.CompilerServices\.[^\]]*\]', '', p).replace(',,', ',').replace(':,', ':').rstrip(',') for p in route['Parameters'].split('|'))
(root / 'tests/VirtualCompany.Api.Tests/Fixtures/FinanceRoutes/internal-finance-routes.json').write_text(json.dumps(routes, indent=2), encoding='utf-8')
(out / 'routes-before-normalized.json').write_text(json.dumps(routes, indent=2), encoding='utf-8')
