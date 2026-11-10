"""Reapply local Unity 6.6 compatibility fixes after reimporting licensed assets."""
import json, pathlib, re
root = pathlib.Path(__file__).resolve().parent.parent
changed = []
for path in (root / 'Assets/Waldemarst/Broccoli').rglob('*.cs'):
    text = path.read_text(encoding='utf-8-sig')
    updated = text.replace('using Unity.VisualScripting;', '')
    updated = re.sub(r'GetInstanceID\s*\(\s*\)', 'GetEntityId().GetHashCode()', updated)
    if text != updated:
        path.write_text(updated, encoding='utf-8'); changed.append(str(path.relative_to(root)))
for path in (root / 'Assets/UniStorm Weather System').rglob('UniStormSystem.cs'):
    text = path.read_text(encoding='utf-8-sig')
    updated = text.replace('UnityEditor.PlayerSettings.virtualRealitySupported', 'UnityEngine.XR.XRSettings.enabled')
    if text != updated:
        path.write_text(updated, encoding='utf-8'); changed.append(str(path.relative_to(root)))
legacy = root / 'Assets/PostProcessing/Editor'
if legacy.is_dir():
    path = legacy / 'LegacyPostProcessing.Editor.asmdef'
    if not path.exists():
        path.write_text(json.dumps({'name':'LegacyPostProcessing.Editor','includePlatforms':['Editor'], 'autoReferenced':False, 'defineConstraints':['ECATY_LEGACY_POSTPROCESSING_EDITOR']}, indent=2))
        changed.append(str(path.relative_to(root)))
report = {'changed': changed, 'vendorSourceRedistributed': False,
          'notes': ['Broccoli integer caches use transient EntityId hashes, not persistent IDs.',
                    'UniStorm legacy VR flag uses XRSettings.',
                    'Obsolete Built-in PostProcessing editor is excluded; URP Volumes remain available.']}
(root / 'Verification/vendor-capability-patches.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
