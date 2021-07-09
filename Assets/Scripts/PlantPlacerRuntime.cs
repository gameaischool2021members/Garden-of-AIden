using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PlantPlacerRuntime : MonoBehaviour
{
    [SerializeField]
    private PlantPlacerModel model = null;

    [SerializeField]
    public float tileWidth = 200f;

    [SerializeField]
    private Terrain targetTerrain = null;

    [SerializeField]
    private GameObject spawnableTreePrefab = null;

    [SerializeField]
    private VegetationPlacer vegetationPlacer;

    [SerializeField]
    private float timeBetweenTreePlacements = 0.1f;

    private List<Vector2Int> queuedUpdates = new List<Vector2Int>();


    public void OnLandscapeUpdated(Vector2Int tileIndex)
    {
        // clear any existing queued instances of this tile
        RemoveExistingQueuedTileGenerations(tileIndex);
        EnqueueTileGeneration(tileIndex);
    }

    private void EnqueueTileGeneration(Vector2Int tileIndex)
    {
        queuedUpdates.Add(tileIndex);
    }

    private void RemoveExistingQueuedTileGenerations(Vector2Int tileIndex)
    {
        queuedUpdates.RemoveAll(tile => tile == tileIndex);
        queuedTreePlacements.RemoveAll(pendingTree => pendingTree.tile == tileIndex);

        if (tileTreeDict.TryGetValue(tileIndex, out var spawnedTrees))
        {
            foreach (var spawnedTree in spawnedTrees)
            {
                spawnedTree.GetComponent<TreeParticles>().BeginDestroyingTree();
            }
        }
        tileTreeDict.Remove(tileIndex);
    }

    private void Start()
    {
        vegetationPlacer = new VegetationPlacer();
        StartCoroutine(RegenerateTiles());
        //SynchronousRegenerateTiles();
        StartCoroutine(PlaceTrees());

        //OnLandscapeUpdated(new Vector2Int((int)tileWidth/2, (int)tileWidth/2));
        ScanWholeMap();
        
    }

    private void SynchronousRegenerateTiles()
    {
        var pythonRunner = new PlantPlacerPythonRunner(model);

        var thisUpdateTile = Vector2Int.zero;

        pythonRunner.StartGenerating(CollectHeightMapAtTile(thisUpdateTile));

        while(!pythonRunner.PollTileGenerationComplete());

        var tileGenerationResults = pythonRunner.CachedPreviousResult;
        EnqueueTreePlacements(thisUpdateTile, tileGenerationResults.relativeTreePositions);
    }

    private IEnumerator RegenerateTiles()
    {
        var pythonRunner = new PlantPlacerPythonRunner(model);
        while (true)
        {
            yield return new WaitUntil(() => queuedUpdates.Count > 0);

            var thisUpdateTile = queuedUpdates.First();
            queuedUpdates.RemoveAt(0);

            pythonRunner.StartGenerating(CollectHeightMapAtTile(thisUpdateTile));

            Debug.Log("Polling for generated tile...");
            yield return new WaitUntil(() => pythonRunner.PollTileGenerationComplete());
            Debug.Log("Generated tile!");

            var tileGenerationResults = pythonRunner.CachedPreviousResult;
            EnqueueTreePlacements(thisUpdateTile, tileGenerationResults.relativeTreePositions);
        }
    }

    private void EnqueueTreePlacements(Vector2Int updateTile, float[,] tileGenerationResults)
    {
        Debug.Log($"Tile co-ords: {updateTile}");
        var tileWorldOrigin = new Vector2(updateTile.x, updateTile.y);
        //var worldPositions = tileGenerationResults.Select(localTreePosition => tileWorldOrigin + localTreePosition);

        var worldPositions = vegetationPlacer.GetVegetationPositionsInWorld(tileGenerationResults, tileWidth/2, tileWorldOrigin);

        queuedTreePlacements.AddRange(worldPositions.Select(treePos => new QueuedTreePlacement{tile=updateTile, treePos=treePos}));
    }

    private IEnumerator PlaceTrees()
    {
        while (true)
        {
            yield return new WaitUntil(() => queuedTreePlacements.Count > 0);
            yield return new WaitForSeconds(timeBetweenTreePlacements);

            var thisNewTree = queuedTreePlacements.First();
            queuedTreePlacements.RemoveAt(0);

            SpawnTree(thisNewTree.tile, thisNewTree.treePos);
        }
    }

    private void SpawnTree(Vector2Int tile, Vector2 treeXzPosition)
    {
        var treeXzPosition3 = new Vector3(treeXzPosition[0], 0f, treeXzPosition[1]);
        var height = targetTerrain.SampleHeight(treeXzPosition3);
        var treePosition = treeXzPosition3 + Vector3.up * height;
        var spawnedTree = Object.Instantiate(spawnableTreePrefab, treePosition, Quaternion.identity);

        if (!tileTreeDict.ContainsKey(tile))
        {
            tileTreeDict.Add(tile, new List<GameObject>());
        }
        var tileSpawnedTrees = tileTreeDict[tile];
        tileSpawnedTrees.Add(spawnedTree);
    }

    private float[,] CollectHeightMapAtTile(Vector2Int centerPoint)
    {
        var heightMapResolution = 256;
        var heightMap = new float[heightMapResolution, heightMapResolution];
        var cornerOrigin2d = centerPoint - Vector2.one * tileWidth / 2f;
        var cornerOrigin = Vector3.right * cornerOrigin2d.x + Vector3.forward * cornerOrigin2d.y;
        for (var x = 0; x < heightMapResolution; ++x)
        {
            for (var y = 0; y < heightMapResolution; ++y)
            {
                var offset = (Vector3.right * x + Vector3.forward * y) * tileWidth / heightMapResolution;
                var pollPosition = cornerOrigin + offset;
                heightMap[x, y] = targetTerrain.SampleHeight(pollPosition);
            }
        }
        
        return heightMap;
    }

    private struct QueuedTreePlacement
    {
        public Vector2Int tile;
        public Vector2 treePos;
    }
    private List<QueuedTreePlacement> queuedTreePlacements = new List<QueuedTreePlacement>();

    private Dictionary<Vector2Int, List<GameObject>> tileTreeDict = new Dictionary<Vector2Int, List<GameObject>>();
    private void ScanWholeMap()
    {
        int halfTileWidth = (int) tileWidth / 2;
        Vector3 dimensions = targetTerrain.terrainData.size;
        for (var x = 0; x < dimensions.x / tileWidth; x++)
        {
            for (var y = 0; y < dimensions.z / tileWidth; y++)
            {
                OnLandscapeUpdated(new Vector2Int((x * (int) tileWidth) + halfTileWidth, (y * (int) tileWidth) + halfTileWidth));
            }
        }
    }

    private List<Vector2> queuedTreePlacements = new List<Vector2>();
}
