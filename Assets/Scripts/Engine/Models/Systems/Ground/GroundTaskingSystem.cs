using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;

namespace Engine.Models
{
    public class GroundTaskingSystem
    {
        private GameManager _gameManager;
        private GroundTaskingCommander _blueForCommander;
        private GroundTaskingCommander _redForCommander;
        
        public GroundTaskingSystem(GameManager gameManager)
        {
            _gameManager = gameManager;
            _blueForCommander = new GroundTaskingCommander(gameManager, Alliance.Bluefor);
            _redForCommander = new GroundTaskingCommander(gameManager, Alliance.Redfor);
        }

        public GroundTaskingCommander GetCommander(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => _blueForCommander,
                Alliance.Redfor => _redForCommander,
                _ => null
            };
        }

        public void OperationalCadenceTurn()
        {
            _blueForCommander.RefreshFront();
            _redForCommander.RefreshFront();
            _blueForCommander.AssignMovementOrders();
            _redForCommander.AssignMovementOrders();
        }

        public void CombatCadenceTurn()
        {
            _blueForCommander.RefreshFront();
            _redForCommander.RefreshFront();
            _blueForCommander.AssignAvailableAdjacentOffensiveAssists();
            _redForCommander.AssignAvailableAdjacentOffensiveAssists();
        }
    }
}
