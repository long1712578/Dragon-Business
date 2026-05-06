using Dragon.Business.Data;
using Dragon.Business.Modules.Payments;
using Microsoft.EntityFrameworkCore;
using RedisFlow.Abstractions;

namespace Dragon.Business.Tests.Modules.Payments;

public class PaymentServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Mock<IPaymentProvider> _mockProvider;
    private readonly Mock<ILogger<PaymentService>> _mockLogger;
    private readonly Mock<IStreamProducer> _mockProducer;
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        // Setup in-memory database for testing
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new AppDbContext(options);
        _dbContext.Database.EnsureCreated();

        // Setup mocks
        _mockProvider = new Mock<IPaymentProvider>();
        _mockProvider.Setup(p => p.ProviderName).Returns("ZaloPay");
        
        _mockLogger = new Mock<ILogger<PaymentService>>();
        _mockProducer = new Mock<IStreamProducer>();

        // Create service under test
        _sut = new PaymentService(
            _dbContext,
            new[] { _mockProvider.Object },
            _mockLogger.Object,
            _mockProducer.Object
        );
    }

    [Fact]
    public async Task CreatePaymentRequestAsync_ShouldCreatePayment_WithValidData()
    {
        // Arrange
        var amount = 100000m;
        var description = "Test payment";
        var expectedUrl = "https://payment.zalopay.vn/test";

        _mockProvider
            .Setup(p => p.CreatePaymentUrlAsync(It.IsAny<Payment>()))
            .ReturnsAsync(expectedUrl);

        // Act
        var result = await _sut.CreatePaymentRequestAsync(amount, description);

        // Assert
        result.Should().NotBeNull();
        result.PaymentUrl.Should().Be(expectedUrl);
        result.ProviderName.Should().Be("ZaloPay");
        result.OrderId.Should().NotBeNullOrEmpty();

        // Verify event was published
        _mockProducer.Verify(
            p => p.ProduceAsync(It.Is<PaymentCreatedEvent>(e => 
                e.Amount == amount && 
                e.Provider == "ZaloPay")),
            Times.Once
        );
    }

    [Fact]
    public async Task CreatePaymentRequestAsync_ShouldThrowException_WhenProviderNotFound()
    {
        // Arrange
        var invalidProvider = "InvalidProvider";

        // Act
        Func<Task> act = async () => await _sut.CreatePaymentRequestAsync(
            100000m, 
            "Test", 
            providerName: invalidProvider
        );

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Provider {invalidProvider} not found");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task CreatePaymentRequestAsync_ShouldHandleInvalidAmounts(decimal invalidAmount)
    {
        // Arrange
        _mockProvider
            .Setup(p => p.CreatePaymentUrlAsync(It.IsAny<Payment>()))
            .ReturnsAsync("https://payment.test");

        // Act
        var result = await _sut.CreatePaymentRequestAsync(invalidAmount, "Test");

        // Assert - Should still create but business logic might validate later
        result.Should().NotBeNull();
        result.OrderId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ProcessWebhookAsync_ShouldReturnFalse_WhenProviderNotFound()
    {
        // Act
        var result = await _sut.ProcessWebhookAsync(
            "InvalidProvider",
            "{}",
            "signature",
            "orderId"
        );

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessWebhookAsync_ShouldReturnFalse_WhenSignatureInvalid()
    {
        // Arrange
        _mockProvider
            .Setup(p => p.VerifyWebhookAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.ProcessWebhookAsync(
            "ZaloPay",
            "{}",
            "invalid-signature",
            "orderId"
        );

        // Assert
        result.Should().BeFalse();
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }
}
