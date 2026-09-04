using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;

namespace Strategy.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private readonly IEnumerable<IBookStrategy> _bookStrategies;

    public WeatherForecastController(IEnumerable<IBookStrategy> bookStrategies)
    {
        _bookStrategies = bookStrategies;
    }
    
    [HttpGet()]
    public async Task<string> Get([FromQuery] string book)
    {
        var (isExist, bookEnum) = BookEnum.CheckIfValidInputAndReturnBook(book);
        
        if (!isExist)
        {
            return $"Book '{book}' is not valid. Valid books are: {string.Join(", ", new List<string> { "The Fellowship of the Ring", "The Two Towers", "The Return of the King" })}";
        }

        var bookStrategy = _bookStrategies.FirstOrDefault(x => x.CanExecute(bookEnum));

        if (bookStrategy != null)
        {
            await bookStrategy.Execute(bookEnum);
            return $"Executed strategy for {bookEnum.Value}";
        }
        else
        {
            return $"No strategy found for {bookEnum.Value}";
        }
        
    }
}

public class BookEnum
{
    private static List<BookEnum> _values = new List<BookEnum>
    {
        FellowshipOfTheRing,
        TwoTowers,
        ReturnOfTheKing
    };
    public BookEnum(string value)
    {
        Value = value;
    }

    private const string TheFellowshipOfTheRing = "The Fellowship of the Ring";
    private const string TheTwoTowers = "The Two Towers";
    private const string TheReturnOfTheKing = "The Return of the King";
    
    public string Value { get; private set; }
    
    public static BookEnum FellowshipOfTheRing => new BookEnum(TheFellowshipOfTheRing);
    public static BookEnum TwoTowers => new BookEnum(TheTwoTowers);
    public static BookEnum ReturnOfTheKing => new BookEnum(TheReturnOfTheKing);

    public static (bool, BookEnum? bookEnum) CheckIfValidInputAndReturnBook(string value)
    {
        var bookEnum = _values.FirstOrDefault(x => x.Value == value);

        return (bookEnum != null, bookEnum);
    }
}

public interface IBookStrategy
{
    bool CanExecute(BookEnum book);
    Task Execute(BookEnum book);
}

public class TheFellowOfTheRingStrategy : IBookStrategy
{
    public bool CanExecute(BookEnum book)
    {
        return book.Value == BookEnum.FellowshipOfTheRing.Value;
    }

    public async Task Execute(BookEnum book)
    {
        await Task.Delay(1000);
        Console.WriteLine($"Executing strategy for {book.Value}");
    }
}

public class TheTwoTowersStrategy : IBookStrategy
{
    public bool CanExecute(BookEnum book)
    {
        return book.Value == BookEnum.TwoTowers.Value;
    }

    public async Task Execute(BookEnum book)
    {
        await Task.Delay(1000);
        Console.WriteLine($"Executing strategy for {book.Value}");
    }
}

public class TheReturnOfTheKingStrategy : IBookStrategy
{
    public bool CanExecute(BookEnum book)
    {
        return book.Value == BookEnum.ReturnOfTheKing.Value;
    }

    public async Task Execute(BookEnum book)
    {
        await Task.Delay(1000);
        Console.WriteLine($"Executing strategy for {book.Value}");
    }
}