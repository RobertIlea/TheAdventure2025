using System.Reflection;
using System.Text.Json;
using Silk.NET.Maths;
using Silk.NET.SDL;
using TheAdventure.Models;
using TheAdventure.Models.Data;
using TheAdventure.Scripting;

namespace TheAdventure;
public enum GameState
{
    Start,
    Running,
    GameOver,
    Win
}
public class Engine
{
    private readonly GameRenderer _renderer;
    private readonly Input _input;
    private readonly ScriptEngine _scriptEngine = new();

    private readonly Dictionary<int, GameObject> _gameObjects = new();
    private readonly Dictionary<string, TileSet> _loadedTileSets = new();
    private readonly Dictionary<int, Tile> _tileIdMap = new();
    private DateTimeOffset _lastEnemyCollision = DateTimeOffset.MinValue;
    private const double EnemyHitCooldownSeconds = 1.0;


    private Level _currentLevel = new();
    private PlayerObject? _player;

    private DateTimeOffset _lastUpdate = DateTimeOffset.Now;

    private bool _hasRespawned = false;
    private bool _hasPrintedGameOver = false;
    private int _heartTextureId = -1;
    private int _gameOverTextureId = -1;
    private int _startScreenTextureId = -1;
    private int _winTextureId = -1;

    private readonly List<EnemyObject> _enemies = new();
    private int _score = 0;
    public Engine(GameRenderer renderer, Input input)
    {
        _renderer = renderer;
        _input = input;

        _input.OnMouseClick += (_, coords) => AddBomb(coords.x, coords.y);
    }

    private GameState _gameState = GameState.Start;
   

    public void SetupWorld()
    {
        _player = new(SpriteSheet.Load(_renderer, "Player.json", "Assets"), 100, 100);
        _heartTextureId = _renderer.LoadTexture("Assets/heart.png", out _); // heart texture
        _gameOverTextureId = _renderer.LoadTexture("Assets/gameover.jpg", out _); // game over texture
        _startScreenTextureId = _renderer.LoadTexture("Assets/start.png", out _); // start texture
        _winTextureId = _renderer.LoadTexture("Assets/won.jpg", out _); // win texture


        var enemySheet = SpriteSheet.Load(_renderer, "Enemy.json", "Assets");
        var random = new Random();
        for (int i = 0; i < 100; i++)
        {
            int x = random.Next(100, 800);
            int y = random.Next(100, 600); 
            _enemies.Add(new EnemyObject(enemySheet, x, y));
        }

        var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));
        var level = JsonSerializer.Deserialize<Level>(levelContent);

        if (level == null)
        {
            throw new Exception("Failed to load level");
        }

        foreach (var tileSetRef in level.TileSets)
        {
            var tileSetContent = File.ReadAllText(Path.Combine("Assets", tileSetRef.Source));
            var tileSet = JsonSerializer.Deserialize<TileSet>(tileSetContent);
            if (tileSet == null)
            {
                throw new Exception("Failed to load tile set");
            }

            foreach (var tile in tileSet.Tiles)
            {
                tile.TextureId = _renderer.LoadTexture(Path.Combine("Assets", tile.Image), out _);
                _tileIdMap.Add(tile.Id!.Value, tile);
            }

            _loadedTileSets.Add(tileSet.Name, tileSet);
        }

        if (level.Width == null || level.Height == null)
        {
            throw new Exception("Invalid level dimensions");
        }

        if (level.TileWidth == null || level.TileHeight == null)
        {
            throw new Exception("Invalid tile dimensions");
        }

        _renderer.SetWorldBounds(new Rectangle<int>(0, 0, level.Width.Value * level.TileWidth.Value,
            level.Height.Value * level.TileHeight.Value));

        _currentLevel = level;

        _scriptEngine.LoadAll(Path.Combine("Assets", "Scripts"));
    }

    public void ProcessFrame()
    {
        var currentTime = DateTimeOffset.Now;
        var msSinceLastFrame = (currentTime - _lastUpdate).TotalMilliseconds;
        _lastUpdate = currentTime;

        if (_gameState == GameState.Win)
        {
            return;
        }

        if (_gameState == GameState.Start)
        {
            if (_input.IsKeyPressed(KeyCode.Return))
            {
                _gameState = GameState.Running;
                Console.WriteLine("Kill all the 100 enemies!");
            }
            return;
        }

        if (_player == null)
        {
            return;
        }

        double up = _input.IsUpPressed() ? 1.0 : 0.0;
        double down = _input.IsDownPressed() ? 1.0 : 0.0;
        double left = _input.IsLeftPressed() ? 1.0 : 0.0;
        double right = _input.IsRightPressed() ? 1.0 : 0.0;
        bool isAttacking = _input.IsKeyAPressed() && (up + down + left + right <= 1);
        bool addBomb = _input.IsKeyBPressed();

        _player.UpdatePosition(up, down, left, right, 48, 48, msSinceLastFrame);
        if (isAttacking)
        {
            _player.Attack();
        }
        
        _scriptEngine.ExecuteAll(this);

        if (addBomb)
        {
            AddBomb(_player.Position.X, _player.Position.Y, false);
        }

        // Respawn the player 3 seconds after death
        if(_player.State.State == PlayerObject.PlayerState.GameOver && (DateTimeOffset.Now -  _lastUpdate).TotalSeconds > 3)
        {
            _player.Respawn(100, 100);
            _hasRespawned = true;
        }

        if (_player.State.State != PlayerObject.PlayerState.GameOver)
        {
            _hasRespawned = false;
        }

        foreach (var enemy in _enemies.ToList())
        {
            enemy.Update(msSinceLastFrame, _player.Position);

            if (!enemy.IsAlive)
            {
                continue;
            }

            var dx = Math.Abs(_player!.Position.X - enemy.Position.X);
            var dy = Math.Abs(_player.Position.Y - enemy.Position.Y);

            if (dx < 32 && dy < 32)
            {
                var now = DateTimeOffset.Now;
                if ((now - _lastEnemyCollision).TotalSeconds > EnemyHitCooldownSeconds)
                {
                    _player.LoseLife();
                    _lastEnemyCollision = now;
                    if(_player.Lives > 0)
                    {
                        Console.WriteLine($"[Player] Lives left: {_player.Lives}");
                    }
                }
            }

            
            if (_player.State.State == PlayerObject.PlayerState.Attack && dx < 48 && dy < 48)
            {
                enemy.Kill();
                _score++;
                Console.WriteLine($"Scor: {_score}");

                if(_score == 100)
                {
                    _gameState = GameState.Win;
                }
            }
        }

    }

    public void RenderFrame()
    {
        _renderer.SetDrawColor(0, 0, 0, 255);
        _renderer.ClearScreen();

        var playerPosition = _player!.Position;
        _renderer.CameraLookAt(playerPosition.X, playerPosition.Y);

        RenderTerrain();
        RenderAllObjects();

        if(_player is not null && _heartTextureId != -1)
        {
            for(int i = 0; i < _player.Lives; i++)
            {
                var dst = new Rectangle<int>(10 + i * 60, 10, 48, 48);
                var src = new Rectangle<int>(0, 0, 347, 347);
                _renderer.RenderTextureScreenSpace(_heartTextureId, src, dst);
            }
        }

        if(_gameState == GameState.Start)
        {
            _renderer.SetDrawColor(0, 0, 0, 255);
            _renderer.RenderFillRectFullScreen();

            if (_startScreenTextureId != -1)
            {
                var (screenW, screenH) = _renderer.ScreenSize;

                var scale = 0.5f;
                _renderer.GetTextureSize(_startScreenTextureId, out int texWidth, out int texHeight);
                var dstW = (int)(texWidth * scale);
                var dstH = (int)(texHeight * scale);
                var dstX = (screenW - dstW) / 2;
                var dstY = (screenH - dstH) / 2;

                _renderer.RenderTextureScreenSpace(
                    _startScreenTextureId,
                    new Rectangle<int>(0, 0, texWidth, texHeight),
                    new Rectangle<int>(dstX, dstY, dstW, dstH)     
                );

            }

            _renderer.PresentFrame();
            return;
        }


        // Gray screen and GameOver message when the player dies
        if(_player is not null && _player.State.State == PlayerObject.PlayerState.GameOver)
        {
            _renderer.SetDrawColor(0, 0, 0, 150);
            _renderer.RenderFillRectFullScreen();

            if (_gameOverTextureId != -1)
            {
                _renderer.GetTextureSize(_gameOverTextureId, out int texW, out int texH);
                var scale = 0.6f;
                var dstW = (int)(texW * scale);
                var dstH = (int)(texH * scale);

                var (screenW, screenH) = _renderer.ScreenSize;
                var dstX = (screenW - dstW) / 2;
                var dstY = (screenH - dstH) / 2;

                _renderer.RenderTextureScreenSpace(
                    _gameOverTextureId,
                    new Rectangle<int>(0, 0, texW, texH),
                    new Rectangle<int>(dstX, dstY, dstW, dstH)
                );

            }

            // Console message
            if (!_hasPrintedGameOver)
            {
                Console.WriteLine("GAME OVER!");
                _hasPrintedGameOver = true;
            }
        }
        else
        {
            _hasPrintedGameOver = false;
        }

        
        if (_score >= 100 && _winTextureId != -1)
        {
            _renderer.SetDrawColor(0, 0, 0, 150);
            _renderer.RenderFillRectFullScreen();

            _renderer.GetTextureSize(_winTextureId, out int texW, out int texH);
            var scale = 0.6f;
            var dstW = (int)(texW * scale);
            var dstH = (int)(texH * scale);

            var (screenW, screenH) = _renderer.ScreenSize;
            var dstX = (screenW - dstW) / 2;
            var dstY = (screenH - dstH) / 2;

            _renderer.RenderTextureScreenSpace(
                _winTextureId,
                new Rectangle<int>(0, 0, texW, texH),
                new Rectangle<int>(dstX, dstY, dstW, dstH)
            );
        }


        _renderer.PresentFrame();
    }

    public void RenderAllObjects()
    {
        var toRemove = new List<int>();
        foreach (var gameObject in GetRenderables())
        {
            gameObject.Render(_renderer);
            if (gameObject is TemporaryGameObject { IsExpired: true } tempGameObject)
            {
                toRemove.Add(tempGameObject.Id);
            }
        }

        foreach (var id in toRemove)
        {
            _gameObjects.Remove(id, out var gameObject);

            if (_player == null)
            {
                continue;
            }

            var tempGameObject = (TemporaryGameObject)gameObject!;
            var deltaX = Math.Abs(_player.Position.X - tempGameObject.Position.X);
            var deltaY = Math.Abs(_player.Position.Y - tempGameObject.Position.Y);
            if (deltaX < 32 && deltaY < 32)
            {
                // Player will lose a life whenever hits a bomb
                _player.LoseLife();
                if(_player.Lives > 0)
                {
                    Console.WriteLine($"[Player] Lives left: {_player.Lives}");
                }
            }
        }

        _player?.Render(_renderer);

        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive) continue;
            enemy.Render(_renderer);
        }
    }

    public void RenderTerrain()
    {
        foreach (var currentLayer in _currentLevel.Layers)
        {
            for (int i = 0; i < _currentLevel.Width; ++i)
            {
                for (int j = 0; j < _currentLevel.Height; ++j)
                {
                    int? dataIndex = j * currentLayer.Width + i;
                    if (dataIndex == null)
                    {
                        continue;
                    }

                    var currentTileId = currentLayer.Data[dataIndex.Value] - 1;
                    if (currentTileId == null)
                    {
                        continue;
                    }

                    var currentTile = _tileIdMap[currentTileId.Value];

                    var tileWidth = currentTile.ImageWidth ?? 0;
                    var tileHeight = currentTile.ImageHeight ?? 0;

                    var sourceRect = new Rectangle<int>(0, 0, tileWidth, tileHeight);
                    var destRect = new Rectangle<int>(i * tileWidth, j * tileHeight, tileWidth, tileHeight);
                    _renderer.RenderTexture(currentTile.TextureId, sourceRect, destRect);
                }
            }
        }
    }

    public IEnumerable<RenderableGameObject> GetRenderables()
    {
        foreach (var gameObject in _gameObjects.Values)
        {
            if (gameObject is RenderableGameObject renderableGameObject)
            {
                yield return renderableGameObject;
            }
        }
    }

    public (int X, int Y) GetPlayerPosition()
    {
        return _player!.Position;
    }

    public void AddBomb(int X, int Y, bool translateCoordinates = true)
    {
        var worldCoords = translateCoordinates ? _renderer.ToWorldCoordinates(X, Y) : new Vector2D<int>(X, Y);

        SpriteSheet spriteSheet = SpriteSheet.Load(_renderer, "BombExploding.json", "Assets");
        spriteSheet.ActivateAnimation("Explode");

        TemporaryGameObject bomb = new(spriteSheet, 2.1, (worldCoords.X, worldCoords.Y));
        _gameObjects.Add(bomb.Id, bomb);
    }
}