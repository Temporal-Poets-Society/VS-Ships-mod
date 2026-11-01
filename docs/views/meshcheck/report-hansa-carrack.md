# Mesh check: hansa-carrack

Mesh file of 2026-10-17 00:45; checked 2026-10-17 00:46 in 33 s. `python tools/shipgen/meshcheck.py hansa-carrack`. Ship-local coordinates in blocks (x to the bow, y up, z to starboard; port is z < 0), as the chalk's notes give them.

Faces: 85711 (82511 skin quads/triangles drawn two-sided, 3200 shape faces drawn one-sided); 59771 visible from somewhere a player can be (7646 standing points on her decks, and all round her).

## Counts

| check | visible | hidden |
|---|---:|---:|
| z-fighting conflict (< 2 mm apart) | 28 (0.009 m2) | 4872 |
| near-coplanar (2-10 mm, warning) | 945 (4.000 m2) | 1650 |
| duplicate (same texture and uvs) | 7 (0.001 m2) | 6 |
| of which conflicts >= 0.001 m2 (fail the check) | 0 | |
| cracks 1.5-50 mm (edges of visible faces) | 8843 (1003.6 blocks long) | - |
| open edges seen from both sides | 1799 | - |
| hull cavity seen (planking faces whose hidden side is reachable) | 0 | - |
| light leaks not at a designed opening | 0 | - |
| light leaks at/next to a designed opening | 3 | - |
| T-junctions on visible faces | 5683 (in 1063 block cells) | - |

Exit status: pass (0 visible conflicts of 0.001 m2 or more).

## Worst visible z-fighting (28 of 28)

| # | where | overlap m2 | gap mm | faces |
|---:|---|---:|---:|---|
| 1 | (4.680, -3.809, -0.101) centreline, in hold | 0.0007 | 1.00 | skin interior quad 3995, wood/debarked/oak, piece of 6 quads spanning x 4.320..4.680, y -3.875..-3.675, z -0.180..0.180 **vs** skin interior quad 4007, black, piece of 2 quads spanning x 4.681..4.681, y -3.824..-3.786, z -0.116..-0.080 |
| 2 | (4.680, -3.809, -0.001) centreline, in hold | 0.0007 | 1.00 | skin interior quad 3995, wood/debarked/oak, piece of 6 quads spanning x 4.320..4.680, y -3.875..-3.675, z -0.180..0.180 **vs** skin interior quad 4011, black, piece of 2 quads spanning x 4.681..4.681, y -3.824..-3.786, z -0.016..0.020 |
| 3 | (4.680, -3.809, 0.099) centreline, in hold | 0.0007 | 1.00 | skin interior quad 3995, wood/debarked/oak, piece of 6 quads spanning x 4.320..4.680, y -3.875..-3.675, z -0.180..0.180 **vs** skin interior quad 4015, black, piece of 2 quads spanning x 4.681..4.681, y -3.824..-3.786, z 0.084..0.120 |
| 4 | (4.320, -3.809, -0.099) centreline, in hold | 0.0007 | 1.00 | skin interior quad 3996, wood/debarked/oak, piece of 6 quads spanning x 4.320..4.680, y -3.875..-3.675, z -0.180..0.180 **vs** skin interior quad 4009, black, piece of 2 quads spanning x 4.319..4.319, y -3.824..-3.786, z -0.120..-0.084 |
| 5 | (4.320, -3.809, 0.101) centreline, in hold | 0.0007 | 1.00 | skin interior quad 3996, wood/debarked/oak, piece of 6 quads spanning x 4.320..4.680, y -3.875..-3.675, z -0.180..0.180 **vs** skin interior quad 4017, black, piece of 2 quads spanning x 4.319..4.319, y -3.824..-3.786, z 0.080..0.116 |
| 6 | (18.528, -0.416, 0.939) starboard, in lowerdeck | 0.0006 | 0.00 | skin interior quad 3376, cloth/reedrope, piece of 15 quads spanning x 18.429..18.735, y -0.716..1.575, z 0.939..1.075 **vs** skin interior quad 3381, cloth/reedrope, piece of 15 quads spanning x 18.429..18.735, y -0.716..1.575, z 0.939..1.075 |
| 7 | (3.026, 3.825, 0.000) centreline | 0.0005 | 0.00 | skin exterior quad 24866, plate/tinbronze, piece of 6 quads spanning x 3.026..3.056, y 3.425..3.825, z -0.275..0.275 **vs** skin exterior quad 24872, plate/tinbronze, piece of 24 quads spanning x 3.013..3.027, y 3.425..3.825, z -0.275..0.275 |
| 8 | (3.026, 3.425, 0.000) centreline | 0.0005 | 0.00 | skin exterior quad 24867, plate/tinbronze, piece of 6 quads spanning x 3.026..3.056, y 3.425..3.825, z -0.275..0.275 **vs** skin exterior quad 24879, plate/tinbronze, piece of 24 quads spanning x 3.013..3.027, y 3.425..3.825, z -0.275..0.275 |
| 9 | (3.026, 3.625, 0.275) centreline | 0.0003 | 0.00 | skin exterior quad 24868, plate/tinbronze, piece of 6 quads spanning x 3.026..3.056, y 3.425..3.825, z -0.275..0.275 **vs** skin exterior quad 24886, plate/tinbronze, piece of 24 quads spanning x 3.013..3.027, y 3.425..3.825, z -0.275..0.275 |
| 10 | (3.026, 3.625, -0.275) centreline | 0.0003 | 0.00 | skin exterior quad 24869, plate/tinbronze, piece of 6 quads spanning x 3.026..3.056, y 3.425..3.825, z -0.275..0.275 **vs** skin exterior quad 24893, plate/tinbronze, piece of 24 quads spanning x 3.013..3.027, y 3.425..3.825, z -0.275..0.275 |
| 11 | (-9.281, 4.875, 4.504) starboard, in greatcabin | 0.0003 | 1.98 | skin exterior quad 1143, zone sterncastle-sides, oak3++3~tar, piece of 25 quads spanning x -15.273..-3.000, y 4.625..4.875, z 3.899..4.610 **vs** skin exterior quad 1209, zone sterncastle-sides, oak4++3~tar, piece of 25 quads spanning x -15.500..-3.000, y 4.875..5.125, z 4.180..4.510 |
| 12 | (-9.281, 4.875, -4.504) port, in greatcabin | 0.0003 | 1.98 | skin exterior quad 1145, zone sterncastle-sides, oak1++3~tar, piece of 25 quads spanning x -15.273..-3.000, y 4.625..4.875, z -4.610..-3.899 **vs** skin exterior quad 1211, zone sterncastle-sides, oak2++3~tar, piece of 25 quads spanning x -15.500..-3.000, y 4.875..5.125, z -4.510..-4.180 |
| 13 | (4.680, -3.795, -0.097) centreline, in hold | 0.0003 | 1.00 | skin interior quad 3995, wood/debarked/oak, piece of 6 quads spanning x 4.320..4.680, y -3.875..-3.675, z -0.180..0.180 **vs** skin interior quad 4008, black, piece of 2 quads spanning x 4.681..4.681, y -3.824..-3.786, z -0.116..-0.080 |
| 14 | (4.680, -3.795, 0.003) centreline, in hold | 0.0003 | 1.00 | skin interior quad 3995, wood/debarked/oak, piece of 6 quads spanning x 4.320..4.680, y -3.875..-3.675, z -0.180..0.180 **vs** skin interior quad 4012, black, piece of 2 quads spanning x 4.681..4.681, y -3.824..-3.786, z -0.016..0.020 |
| 15 | (4.680, -3.795, 0.103) centreline, in hold | 0.0003 | 1.00 | skin interior quad 3995, wood/debarked/oak, piece of 6 quads spanning x 4.320..4.680, y -3.875..-3.675, z -0.180..0.180 **vs** skin interior quad 4016, black, piece of 2 quads spanning x 4.681..4.681, y -3.824..-3.786, z 0.084..0.120 |
| 16 | (4.320, -3.795, -0.103) centreline, in hold | 0.0003 | 1.00 | skin interior quad 3996, wood/debarked/oak, piece of 6 quads spanning x 4.320..4.680, y -3.875..-3.675, z -0.180..0.180 **vs** skin interior quad 4010, black, piece of 2 quads spanning x 4.319..4.319, y -3.824..-3.786, z -0.120..-0.084 |
| 17 | (4.320, -3.795, 0.097) centreline, in hold | 0.0003 | 1.00 | skin interior quad 3996, wood/debarked/oak, piece of 6 quads spanning x 4.320..4.680, y -3.875..-3.675, z -0.180..0.180 **vs** skin interior quad 4018, black, piece of 2 quads spanning x 4.319..4.319, y -3.824..-3.786, z 0.080..0.116 |
| 18 | (-8.771, 4.875, 4.507) starboard, in greatcabin | 0.0001 | 0.93 | skin exterior quad 1146, zone sterncastle-sides, oak4++3~tar, piece of 25 quads spanning x -15.000..-2.375, y 4.625..4.875, z 4.167..4.610 **vs** skin exterior quad 1212, zone sterncastle-sides, oak1++3~tar, piece of 25 quads spanning x -15.000..-2.375, y 4.875..5.125, z 4.244..4.510 |
| 19 | (-8.771, 4.875, -4.507) port, in greatcabin | 0.0001 | 0.93 | skin exterior quad 1148, zone sterncastle-sides, oak2++3~tar, piece of 25 quads spanning x -15.000..-2.375, y 4.625..4.875, z -4.610..-4.167 **vs** skin exterior quad 1214, zone sterncastle-sides, oak3++3~tar, piece of 25 quads spanning x -15.000..-2.375, y 4.875..5.125, z -4.510..-4.244 |
| 20 | (-8.750, 6.500, 3.690) starboard | 0.0001 | 0.00 | skin exterior quad 5314, zone castle-bulwarks, oak3, piece of 13 quads spanning x -8.750..-8.736, y 6.375..9.335, z 3.110..3.726 **vs** skin exterior quad 9503, zone castle-bulwarks, oak4, piece of 24 quads spanning x -9.000..-8.749, y 6.375..6.875, z 3.581..3.726 |
| 21 | (-8.750, 6.500, -3.690) port | 0.0001 | 0.00 | skin exterior quad 5315, zone castle-bulwarks, oak2, piece of 13 quads spanning x -8.750..-8.736, y 6.375..9.335, z -3.726..-3.110 **vs** skin exterior quad 9504, zone castle-bulwarks, oak3, piece of 24 quads spanning x -9.000..-8.749, y 6.375..6.875, z -3.726..-3.581 |
| 22 | (-8.750, 6.745, 3.619) starboard | 0.0001 | 0.02 | skin exterior quad 5316, zone castle-bulwarks, oak2, piece of 14 quads spanning x -8.750..-8.736, y 6.375..9.335, z 3.110..3.726 **vs** skin exterior quad 9527, zone castle-bulwarks, oak1, piece of 25 quads spanning x -9.000..-8.749, y 6.375..7.125, z 3.509..3.726 |
| 23 | (-8.750, 6.745, -3.619) port | 0.0001 | 0.02 | skin exterior quad 5317, zone castle-bulwarks, oak1, piece of 14 quads spanning x -8.750..-8.736, y 6.375..9.335, z -3.726..-3.110 **vs** skin exterior quad 9528, zone castle-bulwarks, oak4, piece of 25 quads spanning x -9.000..-8.749, y 6.375..7.125, z -3.726..-3.509 |
| 24 | (-8.750, 7.000, 3.545) starboard | 0.0001 | 0.00 | skin exterior quad 5318, zone castle-bulwarks, oak1, piece of 15 quads spanning x -8.750..-8.736, y 6.375..9.335, z 3.110..3.726 **vs** skin exterior quad 9551, zone castle-bulwarks, oak2, piece of 25 quads spanning x -9.000..-8.749, y 6.625..7.375, z 3.456..3.654 |
| 25 | (-8.750, 7.000, -3.545) port | 0.0001 | 0.00 | skin exterior quad 5319, zone castle-bulwarks, oak4, piece of 15 quads spanning x -8.750..-8.736, y 6.375..9.335, z -3.726..-3.110 **vs** skin exterior quad 9552, zone castle-bulwarks, oak1, piece of 25 quads spanning x -9.000..-8.749, y 6.625..7.375, z -3.654..-3.456 |
| 26 | (-8.750, 7.250, -3.483) port | 0.0001 | 0.02 | skin exterior quad 5321, zone castle-bulwarks, oak3, piece of 16 quads spanning x -8.750..-8.736, y 6.375..9.335, z -3.726..-3.110 **vs** skin exterior quad 9576, zone castle-bulwarks, oak2, piece of 25 quads spanning x -9.000..-8.750, y 6.875..7.625, z -3.581..-3.404 |
| 27 | (-8.750, 7.250, 3.483) starboard | 0.0001 | 0.02 | skin exterior quad 5320, zone castle-bulwarks, oak4, piece of 16 quads spanning x -8.750..-8.736, y 6.375..9.335, z 3.110..3.726 **vs** skin exterior quad 9575, zone castle-bulwarks, oak3, piece of 25 quads spanning x -9.000..-8.750, y 6.875..7.625, z 3.404..3.581 |
| 28 | (-13.783, 3.375, -4.934) port, in greatcabin | 0.0001 | 0.59 | skin slot:hull quad 322, zone topsides, oak2++3~tar, piece of 13 quads spanning x -14.000..-7.500, y 3.125..3.375, z -5.329..-4.905 **vs** skin slot:hull quad 358, zone sterncastle-sides, oak3++3~tar, piece of 25 quads spanning x -14.000..-3.000, y 3.125..3.625, z -5.330..-4.799 |

## Near-coplanar warnings (worst 20 of 945)

| # | where | overlap m2 | gap mm | faces |
|---:|---|---:|---:|---|
| 1 | (4.500, -3.675, 0.000) centreline, in hold | 0.1296 | 5.00 | skin interior quad 3997, wood/debarked/oak, piece of 6 quads spanning x 4.320..4.680, y -3.875..-3.675, z -0.180..0.180 **vs** skin interior quad 4003, plate/iron, piece of 6 quads spanning x 4.310..4.690, y -3.700..-3.670, z -0.190..0.190 |
| 2 | (-3.700, 5.910, 0.000) centreline | 0.1122 | 3.00 | skin exterior quad 17134, oak2, piece of 6 quads spanning x -3.700..-3.300, y 5.125..6.125, z -0.250..0.250 **vs** skin glass quad 0, glass-clear |
| 3 | (14.125, 4.731, 3.559) starboard, in forecastle | 0.0496 | 10.00 | skin interior quad 4097, zone forecastle-frames, wood/debarked/oak, piece of 11 quads spanning x 14.125..14.125, y 2.125..4.856, z 3.425..4.702 **vs** skin interior quad 4677, zone forecastle-beams, wood/debarked/oak, piece of 18 quads spanning x 13.885..14.115, y 4.375..4.875, z -4.070..4.070 |
| 4 | (14.125, 4.732, -3.559) port, in forecastle | 0.0496 | 10.00 | skin interior quad 4108, zone forecastle-frames, wood/debarked/oak, piece of 11 quads spanning x 14.125..14.125, y 2.125..4.856, z -4.702..-3.425 **vs** skin interior quad 4670, zone forecastle-beams, wood/debarked/oak, piece of 18 quads spanning x 13.885..14.115, y 4.375..4.875, z -4.070..4.070 |
| 5 | (-2.125, 4.732, 3.917) starboard, in greatcabin | 0.0496 | 10.00 | skin interior quad 4075, zone cabin-frames, wood/debarked/oak, piece of 11 quads spanning x -2.125..-2.125, y 2.125..4.856, z 3.768..5.250 **vs** skin interior quad 4667, zone cabin-beams, wood/debarked/oak, piece of 7 quads spanning x -2.115..-2.115, y 4.375..4.875, z -4.424..4.424 |
| 6 | (-2.125, 4.732, -3.917) port, in greatcabin | 0.0496 | 10.00 | skin interior quad 4086, zone cabin-frames, wood/debarked/oak, piece of 11 quads spanning x -2.125..-2.125, y 2.125..4.856, z -5.250..-3.768 **vs** skin interior quad 4663, zone cabin-beams, wood/debarked/oak, piece of 7 quads spanning x -2.115..-2.115, y 4.375..4.875, z -4.424..4.424 |
| 7 | (13.875, 4.731, -3.837) port | 0.0494 | 10.00 | skin exterior quad 19479, zone bulwark-frames, wood/debarked/oak, piece of 45 quads spanning x 13.875..14.125, y 2.125..6.345, z -5.009..-3.246 **vs** skin exterior quad 19687, wood/debarked/oak, piece of 12 quads spanning x 13.885..14.115, y 4.375..4.875, z -4.070..4.070 |
| 8 | (13.875, 4.731, 3.837) starboard | 0.0494 | 10.00 | skin exterior quad 19434, zone bulwark-frames, wood/debarked/oak, piece of 45 quads spanning x 13.875..14.125, y 2.125..6.345, z 3.246..5.009 **vs** skin exterior quad 19685, wood/debarked/oak, piece of 12 quads spanning x 13.885..14.115, y 4.375..4.875, z -4.070..4.070 |
| 9 | (17.500, 17.835, 0.000) centreline | 0.0484 | 10.00 | skin exterior quad 11561, wood/debarked/oak, piece of 6 quads spanning x 17.390..17.610, y 17.835..18.015, z -0.650..0.650 **vs** skin exterior quad 11567, wood/debarked/oak, piece of 6 quads spanning x 17.000..18.000, y 17.845..18.005, z -0.110..0.110 |
| 10 | (13.875, 4.491, 3.921) starboard | 0.0465 | 10.00 | skin exterior quad 19432, zone bulwark-frames, wood/debarked/oak, piece of 45 quads spanning x 13.875..14.125, y 2.125..6.345, z 3.246..5.009 **vs** skin exterior quad 19685, wood/debarked/oak, piece of 12 quads spanning x 13.885..14.115, y 4.375..4.875, z -4.070..4.070 |
| 11 | (13.875, 4.491, -3.921) port | 0.0465 | 10.00 | skin exterior quad 19477, zone bulwark-frames, wood/debarked/oak, piece of 45 quads spanning x 13.875..14.125, y 2.125..6.345, z -5.009..-3.246 **vs** skin exterior quad 19687, wood/debarked/oak, piece of 12 quads spanning x 13.885..14.115, y 4.375..4.875, z -4.070..4.070 |
| 12 | (14.125, 4.499, -3.617) port, in forecastle | 0.0402 | 10.00 | skin interior quad 4107, zone forecastle-frames, wood/debarked/oak, piece of 11 quads spanning x 14.125..14.125, y 2.125..4.856, z -4.702..-3.425 **vs** skin interior quad 4670, zone forecastle-beams, wood/debarked/oak, piece of 18 quads spanning x 13.885..14.115, y 4.375..4.875, z -4.070..4.070 |
| 13 | (14.125, 4.499, 3.617) starboard, in forecastle | 0.0402 | 10.00 | skin interior quad 4096, zone forecastle-frames, wood/debarked/oak, piece of 11 quads spanning x 14.125..14.125, y 2.125..4.856, z 3.425..4.702 **vs** skin interior quad 4677, zone forecastle-beams, wood/debarked/oak, piece of 18 quads spanning x 13.885..14.115, y 4.375..4.875, z -4.070..4.070 |
| 14 | (-2.125, 4.504, 3.984) starboard, in greatcabin | 0.0340 | 10.00 | skin interior quad 4074, zone cabin-frames, wood/debarked/oak, piece of 11 quads spanning x -2.125..-2.125, y 2.125..4.856, z 3.768..5.250 **vs** skin interior quad 4667, zone cabin-beams, wood/debarked/oak, piece of 7 quads spanning x -2.115..-2.115, y 4.375..4.875, z -4.424..4.424 |
| 15 | (-2.125, 4.504, -3.984) port, in greatcabin | 0.0340 | 10.00 | skin interior quad 4085, zone cabin-frames, wood/debarked/oak, piece of 11 quads spanning x -2.125..-2.125, y 2.125..4.856, z -5.250..-3.768 **vs** skin interior quad 4663, zone cabin-beams, wood/debarked/oak, piece of 7 quads spanning x -2.115..-2.115, y 4.375..4.875, z -4.424..4.424 |
| 16 | (18.745, 3.300, 0.415) starboard, in forecastle | 0.0210 | 10.00 | skin interior quad 2828, wood/debarked/oak, piece of 5 quads spanning x 18.745..18.925, y 2.125..3.370, z 0.340..0.500 **vs** skin interior quad 2850, wood/debarked/oak, piece of 6 quads spanning x 18.755..18.915, y 3.230..3.370, z -0.490..0.490 |
| 17 | (18.745, 3.300, -0.415) port, in forecastle | 0.0210 | 10.00 | skin interior quad 2839, wood/debarked/oak, piece of 5 quads spanning x 18.745..18.925, y 2.125..3.370, z -0.500..-0.340 **vs** skin interior quad 2850, wood/debarked/oak, piece of 6 quads spanning x 18.755..18.915, y 3.230..3.370, z -0.490..0.490 |
| 18 | (-12.035, 6.748, -3.897) port, in poopcabin | 0.0176 | 9.61 | skin exterior quad 2207, zone sterncastle-sides, oak3++3~tar, piece of 2 quads spanning x -12.200..-11.800, y 6.625..6.875, z -3.935..-3.859 **vs** skin exterior quad 22858, zone side-windows, wood/debarked/oak, piece of 26 quads spanning x -12.070..-11.930, y 5.900..7.350, z -4.245..-3.734 |
| 19 | (-6.105, -1.865, 4.451) starboard, in hold | 0.0162 | 10.00 | skin interior quad 4193, zone hold-frames, wood/debarked/oak, piece of 6 quads spanning x -6.105..-5.895, y -2.545..-1.855, z 4.039..4.869 **vs** skin interior quad 9004, zone hold-beams, wood/debarked/oak, piece of 4 quads spanning x -6.115..-5.885, y -1.875..-1.125, z 3.500..4.856 |
| 20 | (-5.895, -1.865, 4.451) starboard, in hold | 0.0162 | 10.00 | skin interior quad 4194, zone hold-frames, wood/debarked/oak, piece of 6 quads spanning x -6.105..-5.895, y -2.545..-1.855, z 4.039..4.869 **vs** skin interior quad 9003, zone hold-beams, wood/debarked/oak, piece of 4 quads spanning x -6.115..-5.885, y -1.875..-1.125, z 3.500..4.856 |

## Duplicates (same texture, same uvs; worst 7 of 7)

| # | where | overlap m2 | faces |
|---:|---|---:|---|
| 1 | (-15.841, 4.872, -2.429) port, in greatcabin | 0.0002 | skin exterior quad 2715, zone sterncastle-sides, oak4++3~tar, piece of 49 quads spanning x -15.871..-15.792, y 4.872..4.872, z -3.023..3.023 **vs** skin exterior quad 2728, zone sterncastle-sides, oak4++3~tar, piece of 49 quads spanning x -15.852..-15.000, y 4.872..4.872, z -4.152..4.154 |
| 2 | (-15.841, 4.872, 2.429) starboard, in greatcabin | 0.0002 | skin exterior quad 2711, zone sterncastle-sides, oak3++3~tar, piece of 49 quads spanning x -16.000..-15.792, y 4.872..4.872, z -3.023..3.023 **vs** skin exterior quad 2727, zone sterncastle-sides, oak3++3~tar, piece of 49 quads spanning x -15.852..-15.012, y 4.872..4.872, z -4.152..4.152 |
| 3 | (-15.841, 4.872, 1.607) starboard, in greatcabin | 0.0002 | skin exterior quad 2709, zone sterncastle-sides, oak4++3~tar, piece of 49 quads spanning x -16.273..-15.792, y 4.872..4.872, z -3.023..3.023 **vs** skin exterior quad 2725, zone sterncastle-sides, oak4++3~tar, piece of 49 quads spanning x -15.861..-15.012, y 4.872..4.872, z -3.662..4.152 |
| 4 | (19.048, 2.229, -1.189) port, in forecastle | 0.0001 | skin interior quad 3341, cloth/reedrope, piece of 16 quads spanning x 15.000..19.510, y 2.129..2.336, z -1.325..-1.189 **vs** skin interior quad 3345, cloth/reedrope, piece of 16 quads spanning x 15.000..19.510, y 2.129..2.336, z -1.325..-1.189 |
| 5 | (-15.841, 4.872, 2.515) starboard, in greatcabin | 0.0001 | skin exterior quad 2711, zone sterncastle-sides, oak3++3~tar, piece of 49 quads spanning x -16.000..-15.792, y 4.872..4.872, z -3.023..3.023 **vs** skin exterior quad 2733, zone sterncastle-sides, oak3++3~tar, piece of 49 quads spanning x -15.842..-14.209, y 4.872..4.872, z -4.297..4.297 |
| 6 | (-15.841, 4.872, -2.515) port, in greatcabin | 0.0001 | skin exterior quad 2715, zone sterncastle-sides, oak4++3~tar, piece of 49 quads spanning x -15.871..-15.792, y 4.872..4.872, z -3.023..3.023 **vs** skin exterior quad 2735, zone sterncastle-sides, oak4++3~tar, piece of 49 quads spanning x -15.842..-14.203, y 4.872..4.872, z -4.297..4.297 |
| 7 | (-15.841, 4.872, -1.667) port, in greatcabin | 0.0001 | skin exterior quad 2716, zone sterncastle-sides, oak3++3~tar, piece of 49 quads spanning x -15.871..-15.792, y 4.872..4.872, z -3.023..3.023 **vs** skin exterior quad 2730, zone sterncastle-sides, oak3++3~tar, piece of 49 quads spanning x -15.852..-14.957, y 4.872..4.872, z -4.154..4.162 |

Visible conflict area by the groups involved: interior / interior: 0.005 m2; exterior / exterior: 0.004 m2; slot:hull / slot:hull: 0.000 m2

## Light leaks (rooms flooded on a 0.125 grid, designed openings shut)

- greatcabin: open through a passage more than ~2.0 blocks wide that is not one of the design's openings (not located)
- poopcabin: open through a passage more than ~2.0 blocks wide that is not one of the design's openings (not located)
- forecastle: open through a passage more than ~2.0 blocks wide that is not one of the design's openings (not located)

| # | room | where | width | size (box) | next to |
|---:|---|---|---|---|---|
| 1 | hold | (-4.476, -2.250, 1.062) starboard, in hold | 0.750-1.000 | (-6.039, -2.375, 0.500) - (-2.914, -0.250, 1.625) | well hold |
| 2 | lowerdeck | (-4.476, -0.375, 1.062) starboard, in lowerdeck | 0.750-1.000 | (-6.039, -1.750, 0.500) - (-2.914, -0.250, 1.625) | well hold |
| 3 | lowerdeck | (-1.539, 1.000, 0.062) centreline, in lowerdeck | 0.750-1.000 | (-3.039, 0.875, -0.500) - (-0.039, 2.750, 0.625) | well companion |

## Worst cracks (50 of 8843; a visible face's edge stopping 1.5-50 mm short of the next face)

| # | where | width mm | length | face |
|---:|---|---:|---:|---|
| 1 | (-13.666, -2.075, 0.115) centreline, in hold | 37.1 | 4.591 | skin part:rudder quad 0, oak3++3~tar, piece of 6 quads spanning x -15.669..-11.664, y -4.875..1.113, z -0.150..0.150 |
| 2 | (-14.917, 1.500, -3.226) port, in lowerdeck | 37.7 | 2.057 | skin exterior quad 5890, zone wales, oak4++3~tar, piece of 13 quads spanning x -15.150..-12.734, y 1.375..1.625, z -5.600..-2.224 |
| 3 | (-15.098, 1.755, 3.635) starboard, in lowerdeck | 49.2 | 0.999 | skin exterior quad 6277, zone wales, oak1++3~tar, piece of 16 quads spanning x -15.223..-12.881, y 1.635..1.875, z 2.262..5.598 |
| 4 | (15.963, -4.792, 0.197) centreline, in hold | 46.9 | 1.045 | skin bottom quad 112, oak1++3~tar, piece of 36 quads spanning x 13.000..16.501, y -4.875..-4.708, z -2.060..2.060 |
| 5 | (-10.500, -4.125, 1.894) starboard, in hold | 50.0 | 0.970 | skin bottom quad 506, oak3++3~tar, piece of 47 quads spanning x -12.346..-3.000, y -4.208..-4.042, z -4.688..4.688 |
| 6 | (24.944, 4.250, 0.793) starboard, in forecastle | 36.2 | 1.323 | skin exterior quad 1750, zone forecastle-sides, oak2++3~tar, piece of 12 quads spanning x 21.000..25.523, y 4.125..4.375, z 0.499..2.576 |
| 7 | (-14.021, 1.755, 5.042) starboard, in lowerdeck | 32.3 | 1.341 | skin exterior quad 6295, zone wales, oak3++3~tar, piece of 25 quads spanning x -15.223..-11.002, y 1.635..1.875, z 2.262..5.834 |
| 8 | (-14.724, 1.500, -3.795) port, in lowerdeck | 30.5 | 1.359 | skin exterior quad 5896, zone wales, oak2++3~tar, piece of 16 quads spanning x -15.150..-12.128, y 1.375..1.625, z -5.727..-2.224 |
| 9 | (24.944, 4.250, 0.793) starboard, in forecastle | 40.0 | 0.882 | skin exterior quad 1750, zone forecastle-sides, oak2++3~tar, piece of 12 quads spanning x 21.000..25.523, y 4.125..4.375, z 0.499..2.576 |
| 10 | (24.904, 4.250, -0.814) port, in forecastle | 40.0 | 0.842 | skin exterior quad 1751, zone forecastle-sides, oak1++3~tar, piece of 13 quads spanning x 20.500..25.523, y 4.125..4.375, z -2.768..-0.499 |
| 11 | (-10.038, 1.500, -5.934) port, in lowerdeck | 49.9 | 0.656 | skin exterior quad 5950, zone wales, oak4++3~tar, piece of 25 quads spanning x -13.541..-6.610, y 1.375..1.625, z -6.049..-5.295 |
| 12 | (-14.844, 1.755, 4.171) starboard, in lowerdeck | 33.3 | 0.922 | skin exterior quad 6283, zone wales, oak3++3~tar, piece of 19 quads spanning x -15.223..-12.255, y 1.635..1.875, z 2.262..5.697 |
| 13 | (-11.912, 1.500, -5.734) port, in lowerdeck | 44.4 | 0.684 | skin exterior quad 5932, zone wales, oak2++3~tar, piece of 25 quads spanning x -14.983..-8.450, y 1.375..1.625, z -6.028..-4.059 |
| 14 | (16.559, -2.750, -1.355) port, in hold | 43.5 | 0.695 | skin interior quad 177, zone hold, oak3, piece of 15 quads spanning x 16.000..17.967, y -3.125..-2.625, z -1.929..-0.044 |
| 15 | (-14.021, 1.755, 5.042) starboard, in lowerdeck | 22.5 | 1.341 | skin exterior quad 6295, zone wales, oak3++3~tar, piece of 25 quads spanning x -15.223..-11.002, y 1.635..1.875, z 2.262..5.834 |
| 16 | (-17.469, 7.875, 3.104) starboard, in poopcabin | 39.0 | 0.763 | skin exterior quad 6789, zone castle-mouldings, oak1++3~tar, piece of 15 quads spanning x -17.868..-14.121, y 7.875..8.125, z 2.783..3.676 |
| 17 | (-17.469, 7.875, -3.104) port, in poopcabin | 39.0 | 0.763 | skin exterior quad 6792, zone castle-mouldings, oak2++3~tar, piece of 15 quads spanning x -17.868..-14.121, y 7.875..8.125, z -3.676..-2.783 |
| 18 | (19.639, 3.250, -2.079) port, in forecastle | 46.2 | 0.643 | skin interior quad 1185, zone forecastle-room, oak2, piece of 19 quads spanning x 14.000..21.067, y 2.875..3.375, z -4.326..-0.034 |
| 19 | (-14.592, 1.500, -4.367) port, in lowerdeck | 40.7 | 0.653 | skin exterior quad 178, zone wales, oak1++3~tar, piece of 18 quads spanning x -15.050..-11.000, y 1.375..1.625, z -5.783..-2.224 |
| 20 | (17.451, -2.750, 1.491) starboard, in hold | 50.0 | 0.527 | skin bottom quad 940, oak1++3~tar, piece of 30 quads spanning x 16.000..18.650, y -2.875..-2.625, z -2.771..2.771 |
| 21 | (-14.037, 1.500, 4.129) starboard, in lowerdeck | 43.4 | 0.582 | skin interior quad 818, zone lower-deck, oak3, piece of 21 quads spanning x -14.890..-8.500, y 1.375..1.625, z 1.985..5.348 |
| 22 | (-16.963, 5.500, -3.323) port, in poopcabin | 39.1 | 0.625 | skin exterior quad 1315, zone sterncastle-sides, oak4++3~tar, piece of 16 quads spanning x -17.261..-13.000, y 5.375..5.625, z -4.278..-1.753 |
| 23 | (-15.098, 1.755, 3.635) starboard, in lowerdeck | 48.6 | 0.500 | skin exterior quad 6277, zone wales, oak1++3~tar, piece of 16 quads spanning x -15.223..-12.881, y 1.635..1.875, z 2.262..5.598 |
| 24 | (-14.917, 1.500, -3.226) port, in lowerdeck | 35.1 | 0.686 | skin exterior quad 5890, zone wales, oak4++3~tar, piece of 13 quads spanning x -15.150..-12.734, y 1.375..1.625, z -5.600..-2.224 |
| 25 | (17.054, -2.750, -0.923) port, in hold | 40.0 | 0.599 | skin interior quad 188, zone hold, oak4, piece of 13 quads spanning x 16.000..17.967, y -2.875..-2.625, z -1.929..-0.044 |
| 26 | (-14.544, 2.000, -3.629) port | 40.0 | 0.593 | skin exterior quad 8166, zone lower-deck, oak3, piece of 13 quads spanning x -14.664..-10.000, y 1.875..2.125, z -5.228..-2.927 |
| 27 | (-14.538, 1.755, 4.684) starboard, in lowerdeck | 49.4 | 0.472 | skin exterior quad 6289, zone wales, oak1++3~tar, piece of 22 quads spanning x -15.223..-11.629, y 1.635..1.875, z 2.262..5.774 |
| 28 | (8.500, 3.375, 5.200) starboard | 22.5 | 1.000 | skin exterior quad 912, zone topsides, oak2++3~tar, piece of 9 quads spanning x 6.875..12.000, y 3.125..3.375, z 5.050..5.330 |
| 29 | (8.500, 3.375, 4.900) starboard | 22.5 | 1.000 | skin exterior quad 8493, zone waist-bulwarks, oak1, piece of 13 quads spanning x 6.875..14.000, y 3.125..3.375, z 4.478..5.030 |
| 30 | (8.500, 3.375, -4.900) port | 22.5 | 1.000 | skin exterior quad 8494, zone waist-bulwarks, oak4, piece of 14 quads spanning x 6.875..14.000, y 2.875..3.375, z -5.030..-4.478 |
| 31 | (0.500, 3.375, 5.200) starboard | 22.5 | 1.000 | skin exterior quad 894, zone topsides, oak4++3~tar, piece of 7 quads spanning x 0.000..6.188, y 3.125..3.375, z 5.200..5.330 |
| 32 | (3.500, 3.375, 5.200) starboard | 22.5 | 1.000 | skin exterior quad 900, zone topsides, oak2++3~tar, piece of 7 quads spanning x 0.000..6.188, y 3.125..3.375, z 5.200..5.330 |
| 33 | (4.500, 3.375, 5.200) starboard | 22.5 | 1.000 | skin exterior quad 902, zone topsides, oak4++3~tar, piece of 7 quads spanning x 0.000..6.188, y 3.125..3.375, z 5.200..5.330 |
| 34 | (5.500, 3.375, 5.200) starboard | 22.5 | 1.000 | skin exterior quad 904, zone topsides, oak2++3~tar, piece of 7 quads spanning x 0.000..6.188, y 3.125..3.375, z 5.200..5.330 |
| 35 | (7.500, 3.375, 5.200) starboard | 22.5 | 1.000 | skin exterior quad 910, zone topsides, oak4++3~tar, piece of 9 quads spanning x 6.875..12.000, y 3.125..3.375, z 5.050..5.330 |
| 36 | (0.500, 3.375, 4.900) starboard | 22.5 | 1.000 | skin exterior quad 8475, zone waist-bulwarks, oak3, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z 4.900..5.030 |
| 37 | (0.500, 3.375, -4.900) port | 22.5 | 1.000 | skin exterior quad 8476, zone waist-bulwarks, oak2, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z -5.030..-4.900 |
| 38 | (1.500, 3.375, 4.900) starboard | 22.5 | 1.000 | skin exterior quad 8477, zone waist-bulwarks, oak1, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z 4.900..5.030 |
| 39 | (1.500, 3.375, -4.900) port | 22.5 | 1.000 | skin exterior quad 8478, zone waist-bulwarks, oak4, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z -5.030..-4.900 |
| 40 | (2.500, 3.375, 4.900) starboard | 22.5 | 1.000 | skin exterior quad 8479, zone waist-bulwarks, oak3, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z 4.900..5.030 |
| 41 | (2.500, 3.375, -4.900) port | 22.5 | 1.000 | skin exterior quad 8480, zone waist-bulwarks, oak2, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z -5.030..-4.900 |
| 42 | (3.500, 3.375, 4.900) starboard | 22.5 | 1.000 | skin exterior quad 8481, zone waist-bulwarks, oak1, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z 4.900..5.030 |
| 43 | (3.500, 3.375, -4.900) port | 22.5 | 1.000 | skin exterior quad 8482, zone waist-bulwarks, oak4, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z -5.030..-4.900 |
| 44 | (4.500, 3.375, 4.900) starboard | 22.5 | 1.000 | skin exterior quad 8483, zone waist-bulwarks, oak3, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z 4.900..5.030 |
| 45 | (4.500, 3.375, -4.900) port | 22.5 | 1.000 | skin exterior quad 8484, zone waist-bulwarks, oak2, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z -5.030..-4.900 |
| 46 | (5.500, 3.375, 4.900) starboard | 22.5 | 1.000 | skin exterior quad 8485, zone waist-bulwarks, oak1, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z 4.900..5.030 |
| 47 | (5.500, 3.375, -4.900) port | 22.5 | 1.000 | skin exterior quad 8486, zone waist-bulwarks, oak4, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z -5.030..-4.900 |
| 48 | (7.500, 3.375, 4.900) starboard | 22.5 | 1.000 | skin exterior quad 8491, zone waist-bulwarks, oak3, piece of 15 quads spanning x 6.875..14.000, y 2.875..3.375, z 4.478..5.030 |
| 49 | (7.500, 3.375, -4.900) port | 22.5 | 1.000 | skin exterior quad 8492, zone waist-bulwarks, oak2, piece of 15 quads spanning x 6.875..14.000, y 2.875..3.375, z -5.030..-4.478 |
| 50 | (-16.808, 6.000, -2.675) port, in poopcabin | 39.4 | 0.569 | skin interior quad 1532, zone poop-cabin-walls, oak4, piece of 25 quads spanning x -16.849..-9.000, y 5.625..6.125, z -3.703..-2.267 |

## Open edges seen from both sides (1799 edges, 323.3 blocks; busiest cells)

A two-sided skin face's edge with nothing beyond it and air on both sides: a sheet's free edge (fine for a sail, rope or thin board) or the rim of a hole.

| # | cell centre | edges | length | e.g. |
|---:|---|---:|---:|---|
| 1 | (-15.384, 3.622, -1.500) port, in greatcabin | 48 | 13.85 | skin exterior quad 20756, plate/lead |
| 2 | (-15.384, 3.622, -0.500) port, in greatcabin | 48 | 13.85 | skin exterior quad 20818, plate/lead |
| 3 | (-15.384, 3.622, 0.500) starboard, in greatcabin | 48 | 13.85 | skin exterior quad 20880, plate/lead |
| 4 | (-15.384, 3.622, 1.500) starboard, in greatcabin | 48 | 13.85 | skin exterior quad 20942, plate/lead |
| 5 | (-15.378, 3.597, -2.226) port, in greatcabin | 26 | 6.96 | skin exterior quad 20752, plate/lead |
| 6 | (-15.378, 3.597, 2.226) starboard, in greatcabin | 26 | 6.96 | skin exterior quad 21004, plate/lead |
| 7 | (-11.500, 6.608, 3.702) starboard, in poopcabin | 30 | 6.36 | skin interior quad 8833, plate/lead |
| 8 | (-11.500, 6.608, -3.702) port, in poopcabin | 30 | 6.36 | skin interior quad 8857, plate/lead |
| 9 | (-12.500, 6.608, 3.688) starboard, in poopcabin | 30 | 6.36 | skin interior quad 8825, plate/lead |
| 10 | (-12.500, 6.608, -3.688) port, in poopcabin | 30 | 6.36 | skin interior quad 8849, plate/lead |
| 11 | (-13.500, 6.608, 3.664) starboard, in poopcabin | 30 | 6.36 | skin interior quad 8817, plate/lead |
| 12 | (-13.500, 6.608, -3.664) port, in poopcabin | 30 | 6.36 | skin interior quad 8841, plate/lead |
| 13 | (-16.421, 5.049, -3.491) port | 18 | 5.53 | skin exterior quad 1177, zone sterncastle-sides, oak4++3~tar, piece of 13 quads spanning x -17.150..-12.000, y 4.875..5.125, z -4.475..-3.293 |
| 14 | (-17.321, 6.635, -0.500) port, in poopcabin | 24 | 4.80 | skin exterior quad 21682, plate/lead |
| 15 | (-17.321, 6.635, 0.500) starboard, in poopcabin | 24 | 4.80 | skin exterior quad 21732, plate/lead |

## T-junctions (5683 on visible faces; the busiest block cells)

| # | cell centre | count | e.g. |
|---:|---|---:|---|
| 1 | (12.369, 2.428, -5.395) port | 36 | corner of skin exterior quad 3773, zone topsides, oak4++3~tar, piece of 14 quads spanning x 12.000..12.464, y 2.875..3.375, z -5.287..-5.008 on the edge of skin exterior quad 784, zone topsides, oak2++3~tar, piece of 7 quads spanning x 10.875..14.000, y 2.625..2.875, z -5.498..-4.988 |
| 2 | (12.369, 2.428, 5.395) starboard | 36 | corner of skin exterior quad 3771, zone frieze, oak2++3~tar, piece of 14 quads spanning x 12.000..12.464, y 2.875..3.375, z 5.008..5.287 on the edge of skin exterior quad 782, zone topsides, oak4++3~tar, piece of 7 quads spanning x 10.875..14.000, y 2.625..2.875, z 4.988..5.498 |
| 3 | (-1.655, 3.417, -4.878) port | 35 | corner of skin exterior quad 8568, zone waist-bulwarks, oak1, piece of 24 quads spanning x -2.000..-1.750, y 3.375..3.875, z -4.900..-4.640 on the edge of skin exterior quad 8472, zone waist-bulwarks, oak2, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z -5.030..-4.900 |
| 4 | (-1.655, 3.417, 4.878) starboard | 35 | corner of skin exterior quad 8567, zone waist-bulwarks, oak2, piece of 24 quads spanning x -2.000..-1.750, y 3.375..3.875, z 4.640..4.900 on the edge of skin exterior quad 8471, zone waist-bulwarks, oak3, piece of 9 quads spanning x -2.000..6.188, y 3.125..3.375, z 4.900..5.030 |
| 5 | (0.401, 2.472, -5.616) port | 34 | corner of skin part:gun-port-1-lid quad 4, zone topsides, oak3++3~tar, piece of 16 quads spanning x 0.085..0.915, y 2.335..3.125, z -5.781..-5.410 on the edge of skin part:gun-port-1-lid quad 34, zone topsides, oak2++3~tar, piece of 26 quads spanning x 0.085..0.915, y 2.335..3.125, z -5.781..-5.334 |
| 6 | (0.401, 2.377, 5.704) starboard | 34 | corner of skin part:gun-starboard-1-lid quad 20, zone waist-bulwarks, oak4, piece of 36 quads spanning x 0.085..0.915, y 2.335..3.125, z 5.334..5.781 on the edge of skin part:gun-starboard-1-lid quad 34, zone topsides, oak1++3~tar, piece of 26 quads spanning x 0.085..0.915, y 2.335..3.125, z 5.334..5.781 |
| 7 | (2.401, 2.377, 5.704) starboard | 34 | corner of skin part:gun-starboard-2-lid quad 20, zone waist-bulwarks, oak4, piece of 36 quads spanning x 2.085..2.915, y 2.335..3.125, z 5.334..5.781 on the edge of skin part:gun-starboard-2-lid quad 34, zone topsides, oak4++3~tar, piece of 26 quads spanning x 2.085..2.915, y 2.335..3.125, z 5.334..5.781 |
| 8 | (4.401, 2.472, -5.616) port | 34 | corner of skin part:gun-port-3-lid quad 4, zone topsides, oak3++3~tar, piece of 16 quads spanning x 4.085..4.915, y 2.335..3.125, z -5.781..-5.410 on the edge of skin part:gun-port-3-lid quad 34, zone topsides, oak4++3~tar, piece of 26 quads spanning x 4.085..4.915, y 2.335..3.125, z -5.781..-5.334 |
| 9 | (4.401, 2.377, 5.704) starboard | 34 | corner of skin part:gun-starboard-3-lid quad 20, zone waist-bulwarks, oak4, piece of 36 quads spanning x 4.085..4.915, y 2.335..3.125, z 5.334..5.781 on the edge of skin part:gun-starboard-3-lid quad 34, zone topsides, oak3++3~tar, piece of 26 quads spanning x 4.085..4.915, y 2.335..3.125, z 5.334..5.781 |
| 10 | (8.401, 2.472, -5.616) port | 34 | corner of skin part:gun-port-4-lid quad 4, zone topsides, oak3++3~tar, piece of 16 quads spanning x 8.085..8.915, y 2.335..3.125, z -5.781..-5.410 on the edge of skin part:gun-port-4-lid quad 34, zone topsides, oak3++3~tar, piece of 26 quads spanning x 8.085..8.915, y 2.335..3.125, z -5.781..-5.334 |
| 11 | (8.401, 2.377, 5.704) starboard | 34 | corner of skin part:gun-starboard-4-lid quad 20, zone waist-bulwarks, oak4, piece of 36 quads spanning x 8.085..8.915, y 2.335..3.125, z 5.334..5.781 on the edge of skin part:gun-starboard-4-lid quad 34, zone topsides, oak2++3~tar, piece of 26 quads spanning x 8.085..8.915, y 2.335..3.125, z 5.334..5.781 |
| 12 | (10.475, 2.420, 5.641) starboard | 30 | corner of skin part:gun-starboard-5-lid quad 4, zone topsides, oak3++3~tar, piece of 16 quads spanning x 10.085..10.915, y 2.335..3.125, z 5.346..5.757 on the edge of skin part:gun-starboard-5-lid quad 34, zone topsides, oak1++3~tar, piece of 26 quads spanning x 10.085..10.915, y 2.335..3.125, z 5.270..5.757 |
| 13 | (-8.564, 5.591, 4.250) starboard | 29 | corner of shape:exterior v639e up face (box -9..-8.75, 4.875..5.125, 3.75..4), oak1++0~caulk on the edge of skin exterior quad 9786, zone poop-cabin-walls, oak1, piece of 6 quads spanning x -9.000..-9.000, y 4.875..6.375, z 3.476..4.207 |
| 14 | (2.463, 2.413, -5.661) port | 28 | corner of skin part:gun-port-2-lid quad 4, zone topsides, oak3++3~tar, piece of 16 quads spanning x 2.085..2.915, y 2.335..3.125, z -5.781..-5.410 on the edge of skin part:gun-port-2-lid quad 34, zone topsides, oak1++3~tar, piece of 26 quads spanning x 2.085..2.915, y 2.335..3.125, z -5.781..-5.334 |
| 15 | (10.373, 2.396, -5.659) port | 28 | corner of skin part:gun-port-5-lid quad 4, zone topsides, oak3++3~tar, piece of 16 quads spanning x 10.085..10.915, y 2.335..3.125, z -5.757..-5.346 on the edge of skin part:gun-port-5-lid quad 34, zone topsides, oak2++3~tar, piece of 26 quads spanning x 10.085..10.915, y 2.335..3.125, z -5.757..-5.270 |
