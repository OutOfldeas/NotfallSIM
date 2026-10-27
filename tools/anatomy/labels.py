"""Deutsche Bezeichnungen für BodyParts3D-Teile (für Tooltips im Röntgenmodus)."""
import re

ORD = {'first': '1.', 'second': '2.', 'third': '3.', 'fourth': '4.', 'fifth': '5.', 'sixth': '6.', 'seventh': '7.', 'eighth': '8.',
       'ninth': '9.', 'tenth': '10.', 'eleventh': '11.', 'twelfth': '12.'}
SIDE = {'right': 'rechts', 'left': 'links'}
FIXED = {
    'wall of heart': 'Herz', 'liver': 'Leber', 'spleen': 'Milz', 'stomach': 'Magen', 'pancreas, nsn': 'Pankreas', 'duodenum': 'Duodenum',
    'jejunum': 'Jejunum', 'ileum': 'Ileum', 'colon, nsn': 'Kolon', 'rectum': 'Rektum', 'gallbladder': 'Gallenblase', 'urinary bladder': 'Harnblase',
    'prostate': 'Prostata', 'trachea': 'Trachea', 'bronchus': 'Bronchien', 'esophagus': 'Ösophagus', 'thyroid cartilage': 'Schildknorpel',
    'eyeball': 'Augapfel', 'middle lobe of lung': 'Mittellappen rechte Lunge', 'sacrum': 'Kreuzbein', 'atlas': 'Atlas (HWK 1)', 'axis': 'Axis (HWK 2)',
    'manubrium': 'Manubrium sterni', 'body of sternum': 'Brustbein', 'xiphoid process': 'Schwertfortsatz', 'mandible': 'Unterkiefer',
    'frontal bone': 'Stirnbein', 'occipital bone': 'Hinterhauptsbein', 'sphenoid bone': 'Keilbein', 'ethmoid': 'Siebbein', 'vomer': 'Pflugscharbein',
    'hyoid bone': 'Zungenbein', 'white matter structure of cerebral hemisphere': 'Großhirn', 'cerebellum': 'Kleinhirn', 'pons': 'Pons',
    'medulla oblongata': 'Medulla oblongata', 'midbrain, nsn': 'Mittelhirn', 'corpus callosum': 'Balken', 'optic chiasm': 'Sehnervenkreuzung',
    'ascending aorta': 'Aorta ascendens', 'arch of aorta': 'Aortenbogen', 'descending aorta': 'Aorta descendens', 'brachiocephalic artery, nsn': 'Truncus brachiocephalicus',
    'celiac artery': 'Truncus coeliacus', 'superior mesenteric artery': 'A. mesenterica superior', 'inferior mesenteric artery': 'A. mesenterica inferior',
    'splenic artery': 'A. splenica', 'common hepatic artery': 'A. hepatica communis', 'pulmonary artery': 'Truncus pulmonalis', 'left gastric artery': 'A. gastrica sinistra',
    'superior vena cava': 'V. cava superior', 'inferior vena cava': 'V. cava inferior', 'pulmonary vein': 'Lungenvenen', 'splenic vein': 'V. splenica',
    'superior mesenteric vein': 'V. mesenterica superior',
}
PATTERNS = [
    (r'^(\w+) (\w+) rib$', lambda m: f'{ORD[m[2]]} Rippe {SIDE[m[1]]}'),
    (r'^(\w+) (\w+) costal cartilage$', lambda m: f'{ORD[m[2]]} Rippenknorpel {SIDE[m[1]]}'),
    (r'^(\w+) costal cartilage$', lambda m: f'Rippenknorpel {SIDE[m[1]]}'),
    (r'^(\w+) cervical vertebra$', lambda m: f'HWK {ORD[m[1]].rstrip(".")}'),
    (r'^(\w+) thoracic vertebra$', lambda m: f'BWK {ORD[m[1]].rstrip(".")}'),
    (r'^(\w+) lumbar vertebra$', lambda m: f'LWK {ORD[m[1]].rstrip(".")}'),
    (r'^(right|left) (\w[\w ]*)$', lambda m: f'{BONES.get(m[2], m[2])} {SIDE[m[1]]}'),
    (r'^(upper|lower) lobe of (right|left) lung$', lambda m: f'{"Ober" if m[1] == "upper" else "Unter"}lappen {"rechte" if m[2] == "right" else "linke"} Lunge'),
    (r'phalanx of (right|left) (.+)$', lambda m: f'Phalanx {m[2]} {SIDE[m[1]]}'),
    (r'(.+) of (right|left) foot$', lambda m: f'{BONES.get(m[1], m[1])} {SIDE[m[2]]}'),
]
BONES = {
    'femur': 'Femur', 'tibia': 'Tibia', 'fibula': 'Fibula', 'patella': 'Patella', 'humerus': 'Humerus', 'radius': 'Radius', 'ulna': 'Ulna',
    'clavicle': 'Klavikula', 'scapula': 'Skapula', 'hip bone': 'Hüftbein', 'parietal bone': 'Scheitelbein', 'temporal bone': 'Schläfenbein',
    'maxilla': 'Oberkiefer', 'zygomatic bone': 'Jochbein', 'nasal bone': 'Nasenbein', 'calcaneus': 'Fersenbein', 'talus': 'Sprungbein',
    'kidney': 'Niere', 'adrenal gland': 'Nebenniere', 'ureter': 'Harnleiter', 'lobe of thymus': 'Thymuslappen', 'thalamus': 'Thalamus',
    'optic nerve': 'Sehnerv', 'common carotid artery': 'A. carotis communis', 'subclavian artery': 'A. subclavia', 'renal artery': 'A. renalis',
    'common iliac artery': 'A. iliaca communis', 'external iliac artery': 'A. iliaca externa', 'internal iliac artery': 'A. iliaca interna',
    'brachiocephalic vein': 'V. brachiocephalica', 'internal jugular vein': 'V. jugularis interna', 'subclavian vein': 'V. subclavia',
    'renal vein': 'V. renalis', 'common iliac vein': 'V. iliaca communis', 'external iliac vein': 'V. iliaca externa', 'internal iliac vein': 'V. iliaca interna',
    'navicular bone': 'Kahnbein', 'cuboid bone': 'Würfelbein', 'medial cuneiform bone': 'Keilbein medial', 'intermediate cuneiform bone': 'Keilbein intermedius',
    'lateral cuneiform bone': 'Keilbein lateral', 'scaphoid': 'Kahnbein (Hand)', 'lunate': 'Mondbein', 'triquetral': 'Dreiecksbein', 'pisiform': 'Erbsenbein',
    'trapezium': 'Großes Vieleckbein', 'trapezoid': 'Kleines Vieleckbein', 'capitate': 'Kopfbein', 'hamate': 'Hakenbein',
}


def german(name):
    n = name.strip()
    if n in FIXED: return FIXED[n]
    for pat, fn in PATTERNS:
        m = re.match(pat, n)
        if m:
            try: return fn(m)
            except KeyError: pass
    for k, v in BONES.items():
        if k in n: return n.replace(k, v)
    return n
