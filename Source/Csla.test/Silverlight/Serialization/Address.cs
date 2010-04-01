﻿using System;
using Csla.Xaml;
using Csla.Serialization;
using Csla;

namespace cslalighttest.Serialization
{
  [Serializable]
  public class Address : AddressBase
  {
    public Address()
    {
      MarkAsChild();
    }
    private static readonly PropertyInfo<string> ZipCodeProperty = RegisterProperty(
      typeof(Address),
      new PropertyInfo<string>("ZipCode"));

    public string ZipCode
    {
      get { return GetProperty<string>(ZipCodeProperty); }
      set { SetProperty<string>(ZipCodeProperty, value); }
    }
  }
}
