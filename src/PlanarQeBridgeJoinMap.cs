using PepperDash.Essentials.Core.Bridges;

namespace Pepperdash.Essentials.Plugins.Display.Planar.Qe
{
    public class PlanarQeBridgeJoinMap : DisplayControllerJoinMap
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="joinStart"></param>
        public PlanarQeBridgeJoinMap(uint joinStart)
            : base(joinStart, typeof(PlanarQeBridgeJoinMap))
        {

        }
    }
}