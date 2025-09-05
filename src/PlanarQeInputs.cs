using System;
using System.Collections.Generic;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;

namespace Pepperdash.Essentials.Plugins.Display.Planar.Qe
{
  public class PlanarQeInputs : ISelectableItems<string>
  {
    private Dictionary<string, ISelectableItem> items;
    public Dictionary<string, ISelectableItem> Items
    {
      get => items;
      set
      {
        items = value;
        ItemsUpdated?.Invoke(this, EventArgs.Empty);
      }
    }

    private string currentItem;
    public string CurrentItem
    {
      get => currentItem;
      set
      {
        if (currentItem != value)
        {
          currentItem = value;
          CurrentItemChanged?.Invoke(this, EventArgs.Empty);
        }
      }
    }

    public event EventHandler ItemsUpdated;
    public event EventHandler CurrentItemChanged;

    public PlanarQeInputs()
    {
      CurrentItem = string.Empty;
    }
  }
}
