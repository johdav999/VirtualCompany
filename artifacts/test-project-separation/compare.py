from pathlib import Path
from collections import Counter
import json, re
directory = Path(__file__).resolve().parent
root = directory.parents[1]

def discovered(prefix):
    per_project = {}
    for path in sorted(directory.glob(prefix + '-*.txt')):
        lines = path.read_text(encoding='utf-8-sig').splitlines()
        names = [line.strip() for line in lines if re.match(r'^\s{4}VirtualCompany\.', line)]
        per_project[path.stem.removeprefix(prefix + '-')] = names
    return per_project

before = discovered('before')
after = discovered('after')
old = Counter(name for names in before.values() for name in names)
new = Counter(name for names in after.values() for name in names)
report = {
    'before_discovery_counts': {project: len(names) for project, names in before.items()},
    'after_discovery_counts': {project: len(names) for project, names in after.items()},
    'missing_identities': sorted(set(old) - set(new)),
    'new_identities': sorted(set(new) - set(old)),
    'previously_duplicated_identities_now_unique': sorted(name for name, count in old.items() if count > 1 and new[name] == 1),
    'remaining_duplicates': sorted(name for name, count in new.items() if count > 1),
}
(directory / 'discovery-comparison.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps({key: value if isinstance(value, dict) else len(value) for key, value in report.items()}, indent=2))
if report['missing_identities']:
    print('\n'.join(report['missing_identities'][:20]))
    raise SystemExit('Existing test identities were lost.')
