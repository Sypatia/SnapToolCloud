using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using SnapToolCloud.Models;
using SnapToolCloud.Service;

namespace SnapToolCloud.Controllers
{
    [ApiController]
    [Route("api/assessments")]
    public sealed class AssessmentsController : ControllerBase
    {
        private const string ApiKeyHeaderName = "X-API-Key";
        private readonly IConfiguration _configuration;

        public AssessmentsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost("wind")]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status504GatewayTimeout)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<AssessmentResponse>>> AssessWind([FromBody] WindAssessmentRequest request)
        {
            var authResult = ValidateApiKey();
            if (authResult != null)
                return authResult;

            try
            {
                return Ok(Success(await AssessmentService.AssessWindAsync(request), "Wind assessment completed."));
            }
            catch (AssessmentValidationException ex)
            {
                return BadRequest(Error<AssessmentResponse>($"Invalid wind assessment request. {ex.Message}"));
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    Error<AssessmentResponse>($"Weather forecast service request failed. {ex.Message}"));
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    Error<AssessmentResponse>($"Weather forecast service is not configured or available. {ex.Message}"));
            }
            catch (TaskCanceledException ex)
            {
                return StatusCode(
                    StatusCodes.Status504GatewayTimeout,
                    Error<AssessmentResponse>($"Weather forecast service timed out. {ex.Message}"));
            }
        }

        [HttpPost("wave")]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status504GatewayTimeout)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<AssessmentResponse>>> AssessWave([FromBody] WaveAssessmentRequest request)
        {
            var authResult = ValidateApiKey();
            if (authResult != null)
                return authResult;

            try
            {
                return Ok(Success(await AssessmentService.AssessWaveAsync(request), "Wave assessment completed."));
            }
            catch (AssessmentValidationException ex)
            {
                return BadRequest(Error<AssessmentResponse>($"Invalid wave assessment request. {ex.Message}"));
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    Error<AssessmentResponse>($"Weather forecast service request failed. {ex.Message}"));
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    Error<AssessmentResponse>($"Weather forecast service is not configured or available. {ex.Message}"));
            }
            catch (TaskCanceledException ex)
            {
                return StatusCode(
                    StatusCodes.Status504GatewayTimeout,
                    Error<AssessmentResponse>($"Weather forecast service timed out. {ex.Message}"));
            }
        }

        [HttpPost("passing-vessels")]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<AssessmentResponse>), StatusCodes.Status500InternalServerError)]
        public ActionResult<ApiResponse<AssessmentResponse>> AssessPassingVessels([FromBody] PassingVesselAssessmentRequest request)
        {
            var authResult = ValidateApiKey();
            if (authResult != null)
                return authResult;

            try
            {
                return Ok(Success(AssessmentService.AssessPassingVessels(request), "Passing vessel assessment completed."));
            }
            catch (AssessmentValidationException ex)
            {
                return BadRequest(Error<AssessmentResponse>($"Invalid passing vessel assessment request. {ex.Message}"));
            }
        }

        private ActionResult<ApiResponse<AssessmentResponse>>? ValidateApiKey()
        {
            var expectedApiKey = _configuration["API_KEY"];
            if (string.IsNullOrWhiteSpace(expectedApiKey))
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error<AssessmentResponse>("API key is not configured."));
            }

            var suppliedApiKey = GetSuppliedApiKey();
            if (string.IsNullOrWhiteSpace(suppliedApiKey) || !ApiKeysEqual(expectedApiKey, suppliedApiKey))
            {
                return Unauthorized(Error<AssessmentResponse>("A valid API key is required."));
            }

            return null;
        }

        private string? GetSuppliedApiKey()
        {
            if (Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader))
                return apiKeyHeader.FirstOrDefault();

            if (!Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
                return null;

            const string bearerPrefix = "Bearer ";
            var authorization = authorizationHeader.FirstOrDefault();
            return authorization != null && authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? authorization[bearerPrefix.Length..].Trim()
                : null;
        }

        private static bool ApiKeysEqual(string expectedApiKey, string suppliedApiKey)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey);
            var suppliedBytes = Encoding.UTF8.GetBytes(suppliedApiKey);

            return expectedBytes.Length == suppliedBytes.Length
                && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
        }

        private static ApiResponse<T> Success<T>(T result, string message)
        {
            return new ApiResponse<T>
            {
                Status = "success",
                Result = result,
                Message = message
            };
        }

        private static ApiResponse<T> Error<T>(string message)
        {
            return new ApiResponse<T>
            {
                Status = "error",
                Result = default,
                Message = message
            };
        }
    }
}
