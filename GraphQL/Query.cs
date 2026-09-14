using PizzaStore.Models;

namespace PizzaStore.GraphQL
{
    public class Query
    {
        // Mock data for demonstration purposes
        private static readonly List<Product> _products = new()
    {
        new Product(1, "Laptop", 999.99m, "Electronics"),
        new Product(2, "Smartwatch", 199.99m, "Electronics"),
        new Product(3, "Desk Chair", 149.50m, "Furniture")
    };

        public IEnumerable<Product> GetProducts() => _products;

        public Product? GetProductById(int id) =>
            _products.FirstOrDefault(p => p.Id == id);
    }

}
