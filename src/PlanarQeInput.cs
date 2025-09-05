using System;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;

namespace Pepperdash.Essentials.Plugins.Display.Planar.Qe
{
  public class PlanarQeInput : ISelectableItem
  {
    private bool isSelected;
    public bool IsSelected
    {
      get => isSelected;
      set
      {
        if (isSelected != value)
        {
          isSelected = value;
          ItemUpdated?.Invoke(this, EventArgs.Empty);
        }
      }
    }

    public string Name { get; set; }

    public string Key { get; set; }

    public event EventHandler ItemUpdated;

    public void Select()
    {
      selectAction?.Invoke();
    }

    private readonly Action selectAction;

    public PlanarQeInput(string key, string name, Action selectAction)
    {
      Name = name;
      Key = key;
      IsSelected = false;
      this.selectAction = selectAction;
    }
  }
}
