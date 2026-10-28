"""Auswahl der BodyParts3D-Teile (FMA-IDs) für Haut, Knochen, Organe, Gefäße und Nervensystem.
Quelle: BodyParts3D, (c) The Database Center for Life Science, CC BY-SA 2.1 Japan
(GitHub-Spiegel als STL: https://github.com/Kevin-Mattheus-Moerman/BodyParts3D)."""
import re

SKIN = "FMA7163"

# Namen wie in parts_list_e.txt; Auswahl über reguläre Ausdrücke (englische FMA-Namen)
LAYERS = {
    "bone": [r"\bbone\b(?!.*marrow)", r"vertebra$", r"^atlas$", r"^axis$", r"sacrum", r"\brib$", r"costal cartilage",
             r"manubrium", r"body of sternum", r"xiphoid", r"clavicle", r"scapula$", r"humerus", r"radius$", r"\bulna$",
             r"femur", r"patella", r"tibia$", r"fibula$", r"mandible", r"maxilla", r"vomer", r"ethmoid", r"hyoid",
             r"calcaneus", r"talus", r"cuboid", r"cuneiform", r"navicular", r"metacarpal", r"metatarsal", r"phalanx",
             r"scaphoid", r"lunate", r"triquetral", r"pisiform", r"trapezium", r"trapezoid", r"capitate", r"hamate",
             r"sesamoid"],
    "organ": [r"^wall of heart$", r"lobe of (left |right )?lung", r"^trachea$", r"^bronchus$", r"^esophagus$", r"^liver$",
              r"gallbladder", r"^spleen$", r"^stomach$", r"pancreas, nsn", r"duodenum", r"jejunum", r"ileum",
              r"colon, nsn", r"^rectum$", r"kidney", r"adrenal gland", r"ureter", r"urinary bladder", r"prostate",
              r"eyeball", r"thyroid cartilage", r"lobe of thymus", r"^diaphragm$"],
    "brain": [r"white matter structure of cerebral hemisphere", r"^cerebellum$", r"^pons$", r"medulla oblongata",
              r"midbrain, nsn", r"corpus callosum", r"thalamus$", r"optic nerve", r"optic chiasm",
              r"gyrus$", r"gyri straight gyrus$", r"^(right|left) insula$", r"parietal lobule precuneus$"],  # Rinde (Gyri)
    "artery": [r"aorta", r"brachiocephalic artery", r"common carotid", r"subclavian artery", r"celiac artery",
               r"mesenteric artery", r"renal artery", r"iliac artery", r"splenic artery", r"common hepatic artery",
               r"pulmonary artery", r"gastric artery"],
    "vein": [r"vena cava", r"brachiocephalic vein", r"internal jugular vein", r"subclavian vein", r"renal vein",
             r"iliac vein", r"splenic vein", r"mesenteric vein", r"pulmonary vein"],
}
EXCLUDE = [r"intervertebral disk", r"stylohyoid", r"temporalis", r"zygomaticus", r"nasalis", r"head of", r"conch", r"tibialis", r"fibularis", r"tendon", r"muscle",
           r"opponens", r"palatine", r"lacrimal",
           r"geniohyoid", r"mylohyoid", r"omohyoid", r"sternohyoid", r"thyrohyoid",  # Zungenbeinmuskeln, keine Knochen
           r"stria medullaris"]


# Skelettmuskulatur (BodyParts3D, ca. 240 Teile): Hals, Rumpf, Becken, Arme, Beine. Ohne Gesichts-, Hand- und
# Fußbinnenmuskeln (traumatologisch kaum relevant) und ohne Retinacula/Sehnenbögen.
MUSCLE = (r"biceps|triceps|deltoid|pectoralis|rectus abdominis|rectus femoris|external oblique$|internal oblique$|transversus|"
          r"latissimus|trapezius|gluteus|psoas|iliacus|sartorius|vastus|gastrocnem|soleus|adductor (brevis|longus|magnus|minimus)|"
          r"gracilis|semitend|semimemb|serratus|intercostal muscle|sternocleido|scalenus|longissimus|iliocostalis|multifidus|"
          r"quadratus (lumborum|femoris)|brachialis|brachioradialis|infraspinatus|supraspinatus|subscapularis|teres|rhomboid|"
          r"levator scapulae|tibialis (anterior|posterior)|fibularis|flexor (carpi|digitorum (superficialis|profundus|longus)|"
          r"hallucis longus|pollicis longus)|extensor (carpi|digitorum$|digitorum longus|digiti minimi$|hallucis longus|indicis|"
          r"pollicis)|pronator|supinator|piriformis|obturator|gemellus|pectineus|tensor fasciae|plantaris|popliteus|"
          r"coracobrachialis|anconeus|splenius|semispinalis|coccygeus|iliococcygeus|pubococcygeus|longus colli|digitorum superficialis")
MUSCLE_EXCLUDE = (r"retinaculum|tendinous arch|brevis of|flexor digitorum brevis|extensor (digitorum|hallucis) brevis|hallucis brevis|"
                  r"pollicis brevis|accessorius|masseter|levator (anguli|labii)|rectus capitis|platysma|levatores costarum|\btrapezi(um|oid)$")


def is_muscle(name):
    n = name.lower()
    return bool(re.search(MUSCLE, n)) and not re.search(MUSCLE_EXCLUDE, n)


def select(names):
    """names: dict id -> englischer Name. Rückgabe: dict id -> (layer, name)."""
    out = {}
    for pid, name in names.items():
        n = name.lower()
        if is_muscle(name):
            out[pid] = ('muscle', name)
            continue
        if any(re.search(x, n) for x in EXCLUDE):
            continue
        for layer, pats in LAYERS.items():
            if any(re.search(p, n) for p in pats):
                out[pid] = (layer, name)
                break
    return out
