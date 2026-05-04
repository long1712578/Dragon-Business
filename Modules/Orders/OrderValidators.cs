using FluentValidation;

namespace Dragon.Business.Modules.Orders;

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên sản phẩm không được để trống")
            .MaximumLength(100).WithMessage("Tên sản phẩm không được vượt quá 100 ký tự");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Giá sản phẩm phải lớn hơn 0");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Danh mục không được để trống");
    }
}

public class CreateCafeOrderRequestValidator : AbstractValidator<CreateCafeOrderRequest>
{
    public CreateCafeOrderRequestValidator()
    {
        RuleFor(x => x.TableNumber)
            .NotEmpty().WithMessage("Số bàn không được để trống");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Đơn hàng phải có ít nhất 1 sản phẩm");
    }
}
