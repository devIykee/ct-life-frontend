import copy
import unittest

from placement_repairs import apply_repairs


class PlacementRepairsTest(unittest.TestCase):
    def test_exact_replay_preserves_art_and_is_idempotent(self):
        row = [1, 2, 3, 140, 1.2, .8, .1, .2, .3, 1]
        doc = {'instances': [{'proto': 'house', 'd': row + [1.1, *row[1:]]},
                             {'proto': 'rock', 'd': row.copy()}],
               'buildingBoxes': [[1, 5, 3, 4, 6, 4, 140]]}
        moves = [{'proto': 'house', 'from': [1, 2, 3], 'to': [4, 3, 7]}]
        self.assertEqual(1, apply_repairs(doc, moves))
        self.assertEqual([4, 3, 7] + row[3:], doc['instances'][0]['d'][:10])
        self.assertEqual([1.1, *row[1:]], doc['instances'][0]['d'][10:])
        self.assertEqual(row, doc['instances'][1]['d'])
        self.assertEqual([4, 6, 7, 4, 6, 4, 140], doc['buildingBoxes'][0])
        once = copy.deepcopy(doc)
        self.assertEqual(0, apply_repairs(doc, moves))
        self.assertEqual(once, doc)

    def test_facades_follow_even_when_instance_already_at_destination(self):
        facade = [1, 2, 4, 90, 8, 12, 0, 1]
        doc = {'instances': [], 'wallDecor': [facade.copy()]}
        moves = [{'proto': 'house', 'from': [1, 2, 3], 'to': [4, 3, 7],
                  'facades': [{'from': facade, 'to': [4, 3, 8, *facade[3:]]}]}]
        self.assertEqual(1, apply_repairs(doc, moves))
        self.assertEqual([4, 3, 8, *facade[3:]], doc['wallDecor'][0])
        self.assertEqual(0, apply_repairs(doc, moves))


if __name__ == '__main__':
    unittest.main()
