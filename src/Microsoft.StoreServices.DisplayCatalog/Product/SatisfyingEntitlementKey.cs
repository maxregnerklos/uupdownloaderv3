﻿// Generated by Xamasoft JSON Class Generator
// http://www.xamasoft.com/json-class-generator

using System.Text.Json.Serialization;

namespace Microsoft.StoreServices.DisplayCatalog.Product
{
    public class SatisfyingEntitlementKey
    {
        [JsonPropertyName("EntitlementKeys")]
        public string[] EntitlementKeys;

        [JsonPropertyName("LicensingKeyIds")]
        public string[] LicensingKeyIds;

        [JsonPropertyName("PreOrderReleaseDate")]
        public string PreOrderReleaseDate;
    }
}