using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheAdventure.Models
{
    internal class EnemyObject : RenderableGameObject
    {
        public bool IsAlive { get; private set; } = true;
        private readonly Random _random = new Random();
        private readonly int _moveDirectionX;
        private readonly int _moveDirectionY;
        private double _speed = 100;

        public EnemyObject(SpriteSheet spriteSheet, int x, int y) : base(spriteSheet, (x, y))
        {
            _moveDirectionX = _random.Next(-1, 2); 
            _moveDirectionY = _random.Next(-1, 2);
            SpriteSheet.ActivateAnimation("MoveDown");
        }

        public void Kill()
        {
            IsAlive = false;
        }

        public void Update(double deltaTime, (int X, int Y) playerPosition)
        {
            if (!IsAlive) return;

            var dxToPlayer = playerPosition.X - Position.X;
            var dyToPlayer = playerPosition.Y - Position.Y;
            var distSq = dxToPlayer * dxToPlayer + dyToPlayer * dyToPlayer;

            if (distSq > 300 * 300) return; 

            
            var dist = Math.Sqrt(distSq);
            var dirX = dxToPlayer / dist;
            var dirY = dyToPlayer / dist;

            var dx = (int)(_speed * dirX * (deltaTime / 1000.0));
            var dy = (int)(_speed * dirY * (deltaTime / 1000.0));

            Position = (Position.X + dx, Position.Y + dy);
        }


    }
}
