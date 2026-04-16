using System;
using System.Collections.Generic;
using UnityEngine;
using DifferentGames.Multiplayer.Components;

namespace DifferentGames.Multiplayer.Core
{
    /// <summary>
    /// Core Server-Side visibility engine for DiffNet.
    /// Manages zero-allocation Grid cells using NetworkObject Linked-Lists.
    /// Generates Visibility BitSets (ulong[]) per player.
    /// </summary>
    public sealed class InterestManager
    {
        private readonly NetworkConfig _config;
        private readonly NetworkRunner _runner;

        // Grid Cells: Maps grid coordinate to the first object in that cell's linked list.
        private readonly Dictionary<Vector2Int, NetworkObject> _gridCells = new();
        
        // Caches player's anchor object position (References from NetworkRunner)
        private readonly Dictionary<NetworkPlayerRef, NetworkObject> _playerAnchors;

        // Visibility Maps per player (1 ulong = 64 boolean flags). Array dynamically expands if needed, but rarely.
        // E.g., Array of size 100 handles up to 6400 objects with 0 GC overhead per frame.
        private readonly Dictionary<NetworkPlayerRef, ulong[]> _previousVisibility = new();
        private readonly Dictionary<NetworkPlayerRef, ulong[]> _currentVisibility = new();

        public InterestManager(NetworkRunner runner, NetworkConfig config, Dictionary<NetworkPlayerRef, NetworkObject> playerAnchors)
        {
            _runner = runner;
            _config = config;
            _playerAnchors = playerAnchors;
        }

        // --- Grid Logic ---

        public void AddObject(NetworkObject obj)
        {
            if (!_config.EnableAOI) return;

            Vector2Int cell = GetCell(obj.transform.position);
            obj.CurrentGridCell = cell;

            InsertToGrid(cell, obj);
        }

        public void RemoveObject(NetworkObject obj)
        {
            if (!_config.EnableAOI) return;
            RemoveFromGrid(obj.CurrentGridCell, obj);
        }

        public void UpdateObjectPosition(NetworkObject obj)
        {
            if (!_config.EnableAOI) return;

            Vector2Int newCell = GetCell(obj.transform.position);
            if (newCell != obj.CurrentGridCell)
            {
                RemoveFromGrid(obj.CurrentGridCell, obj);
                InsertToGrid(newCell, obj);
                obj.CurrentGridCell = newCell;
            }
        }

        private Vector2Int GetCell(Vector3 position)
        {
            return new Vector2Int(
                Mathf.FloorToInt(position.x / _config.AOIGridCellSize),
                Mathf.FloorToInt(position.z / _config.AOIGridCellSize)
            );
        }

        private void InsertToGrid(Vector2Int cell, NetworkObject obj)
        {
            if (_gridCells.TryGetValue(cell, out var first))
            {
                obj.NextInGrid = first;
                obj.PrevInGrid = null;
                first.PrevInGrid = obj;
                _gridCells[cell] = obj;
            }
            else
            {
                obj.NextInGrid = null;
                obj.PrevInGrid = null;
                _gridCells[cell] = obj;
            }
        }

        private void RemoveFromGrid(Vector2Int cell, NetworkObject obj)
        {
            if (obj.PrevInGrid != null) obj.PrevInGrid.NextInGrid = obj.NextInGrid;
            else if (_gridCells.TryGetValue(cell, out var first) && first == obj)
            {
                if (obj.NextInGrid != null) _gridCells[cell] = obj.NextInGrid;
                else _gridCells.Remove(cell);
            }

            if (obj.NextInGrid != null) obj.NextInGrid.PrevInGrid = obj.PrevInGrid;

            obj.NextInGrid = null;
            obj.PrevInGrid = null;
        }

        // --- Visibility Logic ---

        /// <summary>
        /// Recalculates the visibility map for a specific player based on their anchor.
        /// </summary>
        public void UpdateVisibilityForPlayer(NetworkPlayerRef player, int maxObjectId)
        {
            if (!_config.EnableAOI) return;

            int requiredArraySize = (maxObjectId / 64) + 1;

            if (!_currentVisibility.TryGetValue(player, out var currentBits))
            {
                currentBits = new ulong[requiredArraySize];
                _currentVisibility[player] = currentBits;
            }
            
            if (!_previousVisibility.TryGetValue(player, out var prevBits))
            {
                prevBits = new ulong[requiredArraySize];
                _previousVisibility[player] = prevBits;
            }
            
            // Resize if maximum ID grew
            if (currentBits.Length < requiredArraySize)
            {
                Array.Resize(ref currentBits, requiredArraySize);
                Array.Resize(ref prevBits, requiredArraySize);
                _currentVisibility[player] = currentBits;
                _previousVisibility[player] = prevBits;
            }

            // Backup old frame, clear current
            Array.Copy(currentBits, prevBits, currentBits.Length);
            Array.Clear(currentBits, 0, currentBits.Length);

            if (!_playerAnchors.TryGetValue(player, out var anchor)) return;

            Vector2Int centerCell = anchor.CurrentGridCell;
            int radius = _config.AOIGridRadius;

            // Iterate 2D grid around player
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    Vector2Int targetCell = new Vector2Int(centerCell.x + x, centerCell.y + y);
                    
                    if (_gridCells.TryGetValue(targetCell, out var currentObj))
                    {
                        // Traverse linked list of this cell
                        while (currentObj != null)
                        {
                            SetBit(currentBits, currentObj.ObjectId.Value);
                            currentObj = currentObj.NextInGrid;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Highly optimized O(1) check if an object is visible to a player.
        /// Accounts for 'Global', 'OwnerOnly' scopes inherently passing through.
        /// </summary>
        public bool IsVisible(NetworkPlayerRef player, NetworkObject obj)
        {
            if (!_config.EnableAOI) return true;

            // 1. Check Global Default & Scoping overrides first (No bits checked = ultra fast)
            var scope = obj.Scoping;
            if (scope == null || scope.Mode == ScopingMode.Global) return true;
            
            if (scope.Mode == ScopingMode.OwnerOnly) return obj.InputAuthority == player;
            
            if (scope.Mode == ScopingMode.Manual) return false; // Handled by custom logic outside

            // 2. Spatial check via BitMap
            if (scope.Mode == ScopingMode.Spatial)
            {
                if (_currentVisibility.TryGetValue(player, out var bits))
                {
                    return GetBit(bits, obj.ObjectId.Value);
                }
                return false;
            }

            return false;
        }
        
        /// <summary>
        /// Check if the object just entered the view this exact tick.
        /// If so, we must forcefully send a Full Delta (bypass baseline diffing).
        /// </summary>
        public bool JustEntered(NetworkPlayerRef player, NetworkObject obj)
        {
            if (!_config.EnableAOI) return false;
            if (obj.Scoping == null || obj.Scoping.Mode != ScopingMode.Spatial) return false;

            if (_currentVisibility.TryGetValue(player, out var currentBits) && 
                _previousVisibility.TryGetValue(player, out var prevBits))
            {
                uint id = obj.ObjectId.Value;
                bool isNowVisible = GetBit(currentBits, id);
                bool wasVisible = GetBit(prevBits, id);
                
                return isNowVisible && !wasVisible;
            }
            return false;
        }

        private void SetBit(ulong[] array, uint index)
        {
            int arrayIdx = (int)(index / 64);
            int bitIdx = (int)(index % 64);
            array[arrayIdx] |= (1UL << bitIdx);
        }

        private bool GetBit(ulong[] array, uint index)
        {
            int arrayIdx = (int)(index / 64);
            int bitIdx = (int)(index % 64);
            if (arrayIdx >= array.Length) return false;
            return (array[arrayIdx] & (1UL << bitIdx)) != 0;
        }
    }
}
