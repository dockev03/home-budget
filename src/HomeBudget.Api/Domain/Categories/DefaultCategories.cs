namespace HomeBudget.Api.Domain.Categories;

/// <summary>Jeu de catégories créé au premier lancement (C6), repris du classeur Excel du foyer.</summary>
public static class DefaultCategories
{
    public sealed record Definition(string Name, CategoryKind Kind, string Icon, string Color);

    public static readonly IReadOnlyList<Definition> All =
    [
        new("Salaire Kévin", CategoryKind.Income, "briefcase", "#2e7d32"),
        new("Salaire Elodie", CategoryKind.Income, "briefcase", "#388e3c"),
        new("CSE / Ndf", CategoryKind.Income, "receipt", "#43a047"),
        new("Primes", CategoryKind.Income, "star", "#66bb6a"),
        new("Cadeaux / Autre", CategoryKind.Income, "gift", "#81c784"),
        new("Allocation CAF", CategoryKind.Income, "family", "#00897b"),
        new("CMG", CategoryKind.Income, "baby", "#26a69a"),
        new("Intérêts", CategoryKind.Income, "percent", "#4db6ac"),

        new("Prêt Immo", CategoryKind.Fixed, "home", "#1565c0"),
        new("Electricité", CategoryKind.Fixed, "flash", "#f9a825"),
        new("Assurance Habitation", CategoryKind.Fixed, "shield-home", "#1976d2"),
        new("LOA MG4", CategoryKind.Fixed, "car", "#1e88e5"),
        new("Assurance auto", CategoryKind.Fixed, "shield-car", "#2196f3"),
        new("Internet", CategoryKind.Fixed, "wifi", "#3949ab"),
        new("Forfait mobile", CategoryKind.Fixed, "cellphone", "#5c6bc0"),
        new("Abonnement musique", CategoryKind.Fixed, "music", "#7e57c2"),
        new("Entretien PaC", CategoryKind.Fixed, "heat-pump", "#00838f"),
        new("CGT", CategoryKind.Fixed, "account-group", "#c62828"),
        new("LOA Matelas", CategoryKind.Fixed, "bed", "#5e35b1"),
        new("Impôts", CategoryKind.Fixed, "bank", "#455a64"),
        new("Gaz", CategoryKind.Fixed, "fire", "#ef6c00"),
        new("Nounou Maël", CategoryKind.Fixed, "baby", "#ec407a"),
        new("Eau", CategoryKind.Fixed, "water", "#0288d1"),
        new("Abonnement IT", CategoryKind.Fixed, "cloud", "#546e7a"),
        new("Frais bancaire", CategoryKind.Fixed, "bank", "#607d8b"),
        new("Abonnement TV", CategoryKind.Fixed, "television", "#6a1b9a"),

        new("Courses", CategoryKind.Variable, "cart", "#e65100"),
        new("Restaurants", CategoryKind.Variable, "food", "#f4511e"),
        new("Essence", CategoryKind.Variable, "gas-station", "#6d4c41"),
        new("Shopping", CategoryKind.Variable, "shopping", "#d81b60"),
        new("Santé", CategoryKind.Variable, "medical", "#e53935"),
        new("Chien", CategoryKind.Variable, "paw", "#8d6e63"),
        new("Cadeaux", CategoryKind.Variable, "gift", "#ab47bc"),
        new("Bricolage", CategoryKind.Variable, "hammer", "#795548"),
        new("IT", CategoryKind.Variable, "laptop", "#78909c"),
        new("Divers", CategoryKind.Variable, "dots", "#9e9e9e"),
        new("Autoroute / Parking", CategoryKind.Variable, "road", "#757575"),
        new("Beauté", CategoryKind.Variable, "lipstick", "#f06292"),
        new("Avance Pro", CategoryKind.Variable, "briefcase-clock", "#8e24aa"),
        new("Transfert CSE / NdF", CategoryKind.Variable, "swap", "#9575cd"),
    ];
}
