"""
The rowing boat's entity, item and recipe (assets/ships/entities/rowboat.json, itemtypes/rowboat.json,
recipes/grid/rowboat.json), from vanilla's sailboat entity with the model rowboat.py writes. Its places (vanilla's
rideable accessories: chests, baskets, vessels, crates, boat seats, the anchor, the figurehead, plaques, shields) are
vanilla's, less the ones on the parts taken out; the rower's thwart is the one seat, and steers.

  python rowboat_assets.py
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ASSETS = os.path.join(ROOT, "assets", "ships")

MATERIAL = {"code": "material", "states": ["aged"], "loadFromProperties": "block/wood"}
TEXTURES = {
    "*-aged": {
        "material": {"base": "game:block/wood/debarked/{material}"},
        "oak": {"base": "game:block/wood/debarked/{material}"},
        "oak1": {"base": "game:block/wood/planks/aged/aged*"},
        "birch1": {"base": "game:block/wood/planks/aged/aged*"},
        "plainoak": {"base": "game:block/wood/debarked/{material}"},
    },
    "*": {
        "material": {"base": "game:block/wood/debarked/{material}"},
        "oak": {"base": "game:block/wood/debarked/{material}"},
        "oak1": {"base": "game:block/wood/planks/{material}*"},
        "birch1": {"base": "game:block/wood/planks/{material}*"},
        "plainoak": {"base": "game:block/wood/debarked/{material}"},
    },
}
# the model's own textures that aren't the wood's
BASE_TEXTURES = {"plain": {"base": "game:block/cloth/linen/plain"}, "iron1": {"base": "game:block/metal/sheet-plain/iron1"},
                 "rope": {"base": "game:item/resource/rope"}, "transparent": {"base": "game:block/transparent"}}
for k in TEXTURES:
    TEXTURES[k].update(BASE_TEXTURES)

STORE = ["seat", "chest", "ropetiepost", "basket", "crate"]


def slot(code, cats, ap, element, behind=None, passthrough=False, parent=""):
    s = {"code": code, "forCategoryCodes": cats, "attachmentPointCode": ap, "stepParentTo": {parent: {"elementName": element}}}
    if behind:
        s["behindSlots"] = behind
    if passthrough:
        s["emptyInteractPassThrough"] = True
    return s


def slots():
    out = [
        slot("Left Prow Storage", ["anchor"], "LProwAP", "LeftProwStorage"),
        slot("Figurehead", ["figurehead", "chiseled"], "FigureheadAP", "FigureHeadStorage"),
        slot("Left Storage 1", STORE, "LStore1AP", "LeftStorage1"),
        slot("Left Storage 4", STORE, "LStore4AP", "LeftStorage4", behind=["Left Storage 5"]),
        slot("Left Storage 5", ["seat", "chest", "basket", "crate"], "LStore5AP", "LeftStorage5"),
        slot("Right Storage 1", STORE, "RStore1AP", "RightStorage1"),
        slot("Right Storage 4", STORE, "RStore4AP", "RightStorage4", behind=["Right Storage 5"]),
        slot("Right Storage 5", ["seat", "chest", "basket", "crate"], "RStore5AP", "RightStorage5"),
        {"code": "RowSeatAP", "forCategoryCodes": []},
        slot("Left Plaque", ["plaque"], "LPlaqueAP", "PlaqueLeftFront"),
        slot("Right Plaque", ["plaque"], "RPlaqueAP", "PlaqueRightFront"),
    ]
    for side, pre in (("Right", "R"), ("Left", "L")):
        for i in (1, 2, 4, 5, 6, 7):          # the third pair went with the midships section
            out.append(slot(f"{side} Shield{i}", ["shield"], f"{pre}Shield{i}AP", f"{pre}Shield{i}SP", passthrough=True,
                            parent="LowerArmL"))
    return out


def selection_boxes(sl):
    """In the same order as the slots (vanilla's rule for boats)."""
    return [s.get("attachmentPointCode") or s["code"] for s in sl]


def entity():
    sl = slots()
    return {
        "code": "rowboat",
        "class": "ships.EntityRowBoat",
        "tags": ["inanimate", "vehicle"],
        "weight": 700,
        "variantgroups": [MATERIAL],
        "attributes": {
            "shouldSwivelFromMotion": False,
            # a pair of oars: livelier than a raft's paddle, slower than a sail
            "speedMultiplier": 1.35,
            "swimmingOffsetY": 1,
            "rightClickPickup": True,
            "mountAnimations": {"idle": "sitboatidle", "ready": "", "forwards": "", "backwards": ""},
        },
        "hitboxSize": {"x": 3, "y": 1.2, "z": 3},
        "behaviorConfigs": {
            "ellipsoidalrepulseagents": {"offset": {"x": 0.1, "z": 2.0}, "radius": {"x": 1.9, "y": 1.5, "z": 4.1}},
            "passivephysicsmultibox": {
                "collisionBoxes": [
                    {"x1": -1.4, "y1": 0, "z1": -0.9, "x2": 1.4, "y2": 1.2, "z2": 1.9},
                    {"x1": -1.4, "y1": 0, "z1": -3.4, "x2": 1.4, "y2": 1.2, "z2": -0.9},
                    {"x1": -1.4, "y1": 0, "z1": -5.9, "x2": 1.4, "y2": 1.2, "z2": -3.4},
                ],
                "groundDragFactor": 1, "airDragFallingFactor": 0.5, "gravityFactor": 1.0,
            },
            "creaturecarrier": {"seats": [{"apName": "RowSeatAP", "bodyYawLimit": 0.4, "controllable": True, "eyeHeight": 1}]},
            "writingsurface": {
                "fontConfig": {"maxWidth": 208, "maxHeight": 96, "fontSize": 20, "textVoxelOffsetY": -1, "textVoxelOffsetZ": 0.25,
                               "textVoxelHeight": 6, "fontName": "Almendra", "verticalAlign": "Middle", "boldFont": True},
                "writingSurfaces": ["LPlaqueAP", "RPlaqueAP"],
            },
            "rideableaccessories": {"dropContentsOnDeath": True, "wearableSlots": sl},
            "selectionboxes": {"selectionBoxes": selection_boxes(sl)},
        },
        "client": {
            "size": 1,
            "renderer": "Shape",
            "shape": {"base": "ships:entity/rowboat", "ignoreElements": ["hideWater"]},
            "animations": [{"code": "row", "animation": "row", "animationSpeed": 1, "easeInSpeed": 4, "easeOutSpeed": 2}],
            "texturesByType": TEXTURES,
            "behaviors": [
                {"code": "ellipsoidalrepulseagents"}, {"code": "passivephysicsmultibox"}, {"code": "interpolateposition"},
                # Sea owns the fitted volumetric cut. A second, depth-writing flat mask cuts the surrounding water.
                {"code": "selectionboxes"},
                {"code": "rideableaccessories"}, {"code": "creaturecarrier"}, {"code": "writingsurface"},
            ],
        },
        "server": {
            "behaviors": [
                {"code": "ellipsoidalrepulseagents"}, {"code": "passivephysicsmultibox"}, {"code": "selectionboxes"},
                {"code": "rideableaccessories"}, {"code": "creaturecarrier"}, {"code": "writingsurface"},
            ],
        },
        "sounds": {},
    }


def item():
    return {
        "code": "rowboat",
        "class": "ships.ItemRowBoat",
        "variantgroups": [MATERIAL],
        "shape": {"base": "ships:entity/rowboat-stowed", "ignoreElements": ["hideWater"]},
        "texturesByType": TEXTURES,
        "heldTpIdleAnimation": "holdunderarm",
        "maxstacksize": 1,
        "materialDensity": 1,
        "liquidSelectable": 1,
        "creativeinventory": {},  # withdrawn from release; old items/entities still load
        "attributes": {"handbook": {"exclude": True}},
        "guiTransform": {"translation": {"x": 3.0, "y": 6.0, "z": 0.0}, "rotation": {"x": -32.0, "y": -35.0, "z": -180.0},
                         "origin": {"x": 0.5, "y": 0.0, "z": 0.5}, "scale": 0.68},
        "groundTransform": {"translation": {"x": 0, "y": 0, "z": 0}, "rotation": {"x": 0, "y": 0, "z": 0},
                            "origin": {"x": 0, "y": 0, "z": 0}, "scale": 1},
        "tpHandTransform": {"translation": {"x": -0.78, "y": 0.48, "z": -0.85}, "rotation": {"x": -8, "y": 143, "z": 2},
                            "origin": {"x": 0.5, "y": 0, "z": 0.5}, "scale": 0.28},
    }


def recipe():
    # the hull from planks of one wood, and a pair of oars (vanilla's)
    return {
        "ingredientPattern": "P_P,PPP,O_O",
        "ingredients": {
            # (the woods the boat comes in: every wood and aged, not the very aged planks)
            "P": {"type": "item", "code": "game:plank-*", "name": "wood", "quantity": 2, "skipVariants": ["veryaged"]},
            "O": {"type": "item", "code": "game:oar-crude-*"},
        },
        "width": 3, "height": 3,
        "output": {"type": "item", "code": "ships:rowboat-{wood}"},
    }


def main():
    # recipe() is historical prototype data; the release deliberately does not emit a craft recipe.
    for rel, obj in (("entities/rowboat.json", entity()), ("itemtypes/rowboat.json", item())):
        path = os.path.join(ASSETS, *rel.split("/"))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(obj, fh, indent="\t")
        print(path)


if __name__ == "__main__":
    main()
