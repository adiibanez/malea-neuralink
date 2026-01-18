using UnityEngine;
using Sensocto.SDK;

namespace Sensocto
{
    /// <summary>
    /// Local interface extending SDK's IMoveReceiver for backward compatibility.
    /// Components can implement either this interface or Sensocto.SDK.IMoveReceiver.
    /// </summary>
    public interface IMoveReceiver : Sensocto.SDK.IMoveReceiver
    {
        // Inherits Move(Vector2 direction) from SDK.IMoveReceiver
    }
}
