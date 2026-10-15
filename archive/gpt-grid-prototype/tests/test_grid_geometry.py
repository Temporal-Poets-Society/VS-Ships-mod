"""Independent geometric invariants, including a hand-calculated sloped face."""
import copy
import importlib.util
import json
from math import sqrt
from pathlib import Path
import sys
import unittest

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/"tools/gridship"))
from geometry import area, cell_bounds, clip_cell, metric_uv, prism, split_solid
from atlas import tiles
module_spec=importlib.util.spec_from_file_location("grid_compile",ROOT/"tools/gridship/compile.py")
compiler=importlib.util.module_from_spec(module_spec)
module_spec.loader.exec_module(compiler)

class GeometryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.spec=json.loads((ROOT/"designs/two-bay.json").read_text())
        cls.data=compiler.compile_blueprint(cls.spec)

    def test_diagonal_texture_is_metric(self):
        face=[(0,0,0),(1,0,0),(1,1,2),(0,1,2)]
        uv=metric_uv(face)
        self.assertAlmostEqual(abs(uv[2][1]-uv[1][1])/16,sqrt(5),places=7)
        self.assertAlmostEqual(abs(uv[1][0]-uv[0][0])/16,1)

    def test_projected_panel_is_split_in_real_3d(self):
        solid=prism(0,1,[(0,0),(1,2),(1,1.75),(0,-.25)])
        parts=list(split_solid(solid,[0,0,0]))
        self.assertGreater(len({c[2] for c,_ in parts}),1)
        for cell,faces in parts:
            bounds=cell_bounds(cell,[0,0,0])
            for face in faces:
                for p in face:
                    self.assertTrue(all(bounds[a]-1e-8<=p[a]<=bounds[a+3]+1e-8 for a in range(3)))

    def test_ownership_and_uv_invariants(self):
        stats=compiler.validate_compiled(self.data)
        self.assertGreater(stats["cellsWithMoreThanSixPlanes"],0)
        self.assertLess(stats["maxTextureDistanceError"],1e-6)
        self.assertGreater(stats["collisionVoxels"],0)

    def test_visible_rib_width_does_not_change_cell_identity(self):
        broad=copy.deepcopy(self.spec); broad["ribVisibleWidth"]=1
        data=compiler.compile_blueprint(broad)
        self.assertEqual({c["id"] for c in self.data["cells"]},{c["id"] for c in data["cells"]})
        self.assertNotEqual(sum(area(f["points"]) for c in self.data["cells"] for f in c["faces"] if f["kind"]=="rib"),
                            sum(area(f["points"]) for c in data["cells"] for f in c["faces"] if f["kind"]=="rib"))

    def test_design_data_changes_bay_spacing(self):
        dense=copy.deepcopy(self.spec)
        dense.update(lengthCells=5,clearBayCells=1,ribCells=[0,2,4])
        dense["stair"]["foot"][0]=1; dense["hatch"]["cell"][0]=3
        data=compiler.compile_blueprint(dense)
        self.assertEqual(max(c["cell"][0] for c in data["cells"]),4)

    def test_invalid_gap_is_rejected(self):
        bad=copy.deepcopy(self.spec); bad["ribCells"]=[0,3,8]
        with self.assertRaises(AssertionError): compiler.compile_blueprint(bad)

    def test_atlas_tiles_preserve_area_and_density(self):
        points=[(0,0,0),(1,0,0),(1,1,2),(0,1,2)]
        pieces=list(tiles(points,metric_uv(points)))
        self.assertGreater(len(pieces),1)
        self.assertAlmostEqual(sum(area(p) for p,_ in pieces),sqrt(5),places=7)
        for p,uv in pieces:
            self.assertTrue(all(-1e-7<=v<=16+1e-7 for corner in uv for v in corner))
            for i,a in enumerate(p):
                k=(i+1)%len(p)
                physical=sqrt(sum((p[k][j]-a[j])**2 for j in range(3)))
                textured=sqrt(sum((uv[k][j]-uv[i][j])**2 for j in range(2)))/16
                self.assertAlmostEqual(physical,textured,places=7)

    def test_beam_ends_and_deck_edges_stay_inside_hull(self):
        for c in self.data["cells"]:
            for face in c["faces"]:
                if face["kind"] not in ("deck","beam"): continue
                for p in face["points"]:
                    width=compiler.section_width(self.spec,p[1])-self.spec["shellThicknessAcross"]
                    self.assertLessEqual(abs(p[2]),width+1e-6)

    def test_compilation_is_deterministic(self):
        self.assertEqual(self.data,compiler.compile_blueprint(copy.deepcopy(self.spec)))

if __name__=="__main__": unittest.main()
