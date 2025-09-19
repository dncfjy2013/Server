using System;

namespace AutoExposingServiceFramework.Business
{

    public class DataResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public DateTime ProcessedTime { get; set; }
        public int ResultData { get; set; }
    }
}
