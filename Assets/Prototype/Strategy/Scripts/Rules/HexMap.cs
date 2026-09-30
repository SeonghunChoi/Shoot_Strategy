using System;
using System.Collections.Generic;

namespace Prototype.Strategy.Rules
{
    public enum TerrainType
    {
        Plain,
        Mountain,
        Sea,
        Coast,
        Desert,
    }

    public struct HexCell
    {
        public TerrainType Terrain;
        /// <summary>유전 입지 표식 (정유시설 건설 조건).</summary>
        public bool Oil;
    }

    /// <summary>
    /// 사각형 odd-r 오프셋으로 저장한 헥스 맵 (기술 설계 4.2의 평면 배열).
    /// 렌더링 방식은 모른다.
    /// </summary>
    public sealed class HexMap
    {
        readonly HexCell[] _cells;

        public int Width { get; }
        public int Height { get; }

        public HexMap(int width, int height)
        {
            Width = width;
            Height = height;
            _cells = new HexCell[width * height];
        }

        public bool Contains(Hex h)
        {
            var (col, row) = h.ToOffset();
            return col >= 0 && col < Width && row >= 0 && row < Height;
        }

        public ref HexCell this[Hex h]
        {
            get
            {
                var (col, row) = h.ToOffset();
                if (col < 0 || col >= Width || row < 0 || row >= Height)
                    throw new ArgumentOutOfRangeException(nameof(h), $"맵 밖의 헥스 {h}");
                return ref _cells[row * Width + col];
            }
        }

        public IEnumerable<Hex> AllHexes()
        {
            for (var row = 0; row < Height; row++)
            for (var col = 0; col < Width; col++)
                yield return Hex.FromOffset(col, row);
        }

        /// <summary>
        /// 문자 격자로 맵을 만든다. 한 줄이 한 행(위에서 아래), 한 글자가 한 칸.
        /// . 평지  ^ 산악  ~ 해상  , 해안  : 사막  * 평지+유전  o 사막+유전
        /// </summary>
        public static HexMap Parse(IReadOnlyList<string> rows)
        {
            if (rows.Count == 0) throw new ArgumentException("빈 맵");
            var width = rows[0].Length;
            var map = new HexMap(width, rows.Count);
            for (var row = 0; row < rows.Count; row++)
            {
                if (rows[row].Length != width)
                    throw new ArgumentException($"{row}행 길이 {rows[row].Length} (기대값 {width})");
                for (var col = 0; col < width; col++)
                    map._cells[row * width + col] = ParseCell(rows[row][col], col, row);
            }
            return map;
        }

        static HexCell ParseCell(char c, int col, int row) => c switch
        {
            '.' => new HexCell { Terrain = TerrainType.Plain },
            '^' => new HexCell { Terrain = TerrainType.Mountain },
            '~' => new HexCell { Terrain = TerrainType.Sea },
            ',' => new HexCell { Terrain = TerrainType.Coast },
            ':' => new HexCell { Terrain = TerrainType.Desert },
            '*' => new HexCell { Terrain = TerrainType.Plain, Oil = true },
            'o' => new HexCell { Terrain = TerrainType.Desert, Oil = true },
            _ => throw new ArgumentException($"알 수 없는 지형 문자 '{c}' ({col}, {row})"),
        };
    }
}
