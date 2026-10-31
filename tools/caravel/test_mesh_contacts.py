import math
import unittest
import numpy as np
import overlap_check as oc
from mesh_contacts import trim_contacts
from surface_uv import wood_uv


def quad(x0,x1):
    q=[0]
    for x,y in ((x0,0),(x1,0),(x1,1),(x0,1)):
        q.extend((x,y,0,x*16,y*16,0,0,1))
    return q


class Contacts(unittest.TestCase):
    def test_conditional_reveal(self):
        m={'groups':{'slot:hull':[quad(0,1),quad(.5,1.5)]},'slotQuads':{'slot:hull':[0,-1,1,-1]}}
        empty={'deck':set(),'hull':set()}
        clashes=oc.find_clashes(m,empty)
        self.assertEqual(len(clashes),1)
        self.assertEqual(trim_contacts(m,clashes),1)
        for state,area in ((set(),1.5),({0},1),({1},1),({0,1},0)):
            e={'deck':set(),'hull':state}
            self.assertEqual(oc.find_clashes(m,e),[])
            self.assertAlmostEqual(sum(oc.overlap_area(p,p,n) for _,_,p,n in oc.polys(m,e)),area,places=6)

    def test_metric_uv_on_oblique_faces(self):
        n=np.array([.3,.4,.5]);n/=np.linalg.norm(n)
        for grain in range(3):
            edge=np.cross(n,[0,1,0]);edge/=np.linalg.norm(edge)
            p=np.array([1300,2700,-900]);q=p+edge*2500
            a,b=wood_uv(p,n,grain),wood_uv(q,n,grain)
            self.assertAlmostEqual(math.dist(a,b),40,places=7)


if __name__=='__main__': unittest.main()
