using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputSingleton : Singleton<InputSingleton>
{
    public InputSystem_Actions ActionAsset;

    public override void Awake()
    {
        base.Awake();
        ActionAsset = new InputSystem_Actions();
        ActionAsset.Enable();
    }
    public virtual void OnEnable()
    {
        ActionAsset?.Enable();
    }

    public void SetActionAsset(InputSystem_Actions newValue)
    {
        ActionAsset.Disable();
        ActionAsset.LoadBindingOverridesFromJson(newValue.SaveBindingOverridesAsJson());
        ActionAsset.Enable();
    }
}