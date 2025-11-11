// FILEPATH: Assets/Scripts/Gate/GateCell.cs
using UnityEngine;

/// <summary>
/// Tag + index holder for a gate cell, routes paint to its GateGrid.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class GateCell : MonoBehaviour
{
    private GateGrid _grid;
    private int _row, _col;

    public void Init(GateGrid grid, int row, int col)
    {
        _grid = grid;
        _row  = row;
        _col  = col;
    }

    public GateGrid Grid => _grid;
    public int Row => _row;
    public int Col => _col;
}