"""Regression checks for tiled hull faces with a redundant corner on an edge."""
import unittest
import numpy as np
import overlap_check as OC


def quad(points, normal):
    return [0] + [v for p in points for v in (*p, 0, 0, *normal)]


class TiledFacePlanes(unittest.TestCase):
    def setUp(self):
        self.points = np.array([[8.7953, -.625, -.102], [8.8945, -.4543, -.102],
                                [8.9405, -.375, -.102], [8.8809, -.4543, -.1431]])
        self.normal = np.array([.831, -.483, -.275])
        self.face = quad(self.points, self.normal)

    def clashes(self, other):
        return OC.find_clashes({'groups': {'copper': [self.face], 'other': [other]}}, {'deck': set(), 'hull': set()})

    def test_redundant_corner_does_not_make_an_oblique_face_a_flat_one(self):
        flat = quad([[8.5, -.8, -.102], [9, -.8, -.102], [9, .1, -.102], [8.5, .1, -.102]], [0, 0, -1])
        self.assertEqual(self.clashes(flat), [])

    def test_real_overlap_of_the_oblique_face_is_found(self):
        other = quad(self.points + self.normal * .0005, self.normal)
        self.assertEqual(len(self.clashes(other)), 1)


if __name__ == '__main__':
    unittest.main()
