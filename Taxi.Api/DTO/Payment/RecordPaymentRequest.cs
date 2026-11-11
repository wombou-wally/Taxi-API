using System.Text.Json.Serialization;
using Taxi.Api.Models;

namespace Taxi.Api.DTO.Payment
{
    public class RecordPaymentRequest
    {
        
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public required PaymentMethod Method { get; set; }
    }
}