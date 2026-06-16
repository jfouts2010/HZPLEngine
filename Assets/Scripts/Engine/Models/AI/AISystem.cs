using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;

namespace Engine.Models
{
    public class AISystem
    {
        private GameManager _gameManager;
        private AllianceAI _blueForAI;
        private AllianceAI _redForAI;
        
        public AISystem(GameManager gameManager)
        {
            _gameManager = gameManager;
            _blueForAI = new AllianceAI(gameManager, Alliance.Bluefor);
            _redForAI = new AllianceAI(gameManager, Alliance.Redfor);
        }

        public AllianceAI GetAllianceAI(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => _blueForAI,
                Alliance.Redfor => _redForAI,
                _ => null
            };
        }

        public void GameTurn()
        {
            _blueForAI.RefreshFront();
            _redForAI.RefreshFront();
        }
    }
}