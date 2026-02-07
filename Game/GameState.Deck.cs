partial class GameState
{
    private static readonly CardSuit[] AllSuits = { CardSuit.Spades, CardSuit.Hearts, CardSuit.Diamonds, CardSuit.Clubs };

    private void BuildDeck()
    {
        _drawPile.Clear();
        _discardPile.Clear();

        var dc = HasExpansion(Expansion.DodgeCity);
        var rounds = dc ? 3 : 2; // 3 rounds = 156 suit/values for larger deck

        var suitValuePool = new List<(CardSuit Suit, int Value)>();
        for (var round = 0; round < rounds; round++)
        {
            foreach (var suit in AllSuits)
            {
                for (var value = 2; value <= 14; value++)
                {
                    suitValuePool.Add((suit, value));
                }
            }
        }
        var shuffledSV = new Queue<(CardSuit, int)>(suitValuePool.OrderBy(_ => _random.Next()));

        var cards = new List<Card>();

        // Base game cards
        cards.AddRange(CreateCards(CardType.Bang, 22, shuffledSV));
        cards.AddRange(CreateCards(CardType.Missed, 12, shuffledSV));
        cards.AddRange(CreateCards(CardType.Beer, 6, shuffledSV));
        cards.AddRange(CreateCards(CardType.Gatling, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.Stagecoach, 4, shuffledSV));
        cards.AddRange(CreateCards(CardType.CatBalou, 4, shuffledSV));
        cards.AddRange(CreateCards(CardType.Indians, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.Duel, 3, shuffledSV));
        cards.AddRange(CreateCards(CardType.Panic, 4, shuffledSV));
        cards.AddRange(CreateCards(CardType.Saloon, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.WellsFargo, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.GeneralStore, 3, shuffledSV));
        cards.AddRange(CreateCards(CardType.Barrel, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.Mustang, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.Scope, 1, shuffledSV));
        cards.AddRange(CreateCards(CardType.Volcanic, 2, shuffledSV));
        cards.AddRange(CreateCards(CardType.Schofield, 3, shuffledSV));
        cards.AddRange(CreateCards(CardType.Remington, 1, shuffledSV));
        cards.AddRange(CreateCards(CardType.RevCarabine, 1, shuffledSV));
        cards.AddRange(CreateCards(CardType.Winchester, 1, shuffledSV));
        cards.AddRange(CreateCards(CardType.Jail, 1, shuffledSV));
        cards.AddRange(CreateCards(CardType.Dynamite, 1, shuffledSV));

        // Dodge City cards
        if (dc)
        {
            cards.AddRange(CreateCards(CardType.Punch, 3, shuffledSV));
            cards.AddRange(CreateCards(CardType.Springfield, 2, shuffledSV));
            cards.AddRange(CreateCards(CardType.Dodge, 2, shuffledSV));
            cards.AddRange(CreateCards(CardType.Whisky, 2, shuffledSV));
            cards.AddRange(CreateCards(CardType.Tequila, 2, shuffledSV));
            cards.AddRange(CreateCards(CardType.RagTime, 2, shuffledSV));
            cards.AddRange(CreateCards(CardType.Brawl, 2, shuffledSV));
            cards.AddRange(CreateCards(CardType.CanCan, 1, shuffledSV));
            cards.AddRange(CreateCards(CardType.Conestoga, 1, shuffledSV));
            cards.AddRange(CreateCards(CardType.Derringer, 1, shuffledSV));
            cards.AddRange(CreateCards(CardType.Pepperbox, 1, shuffledSV));
            cards.AddRange(CreateCards(CardType.Howitzer, 1, shuffledSV));
            cards.AddRange(CreateCards(CardType.BuffaloRifle, 1, shuffledSV));
            cards.AddRange(CreateCards(CardType.Canteen, 1, shuffledSV));
            cards.AddRange(CreateCards(CardType.Bible, 1, shuffledSV));
            cards.AddRange(CreateCards(CardType.IronPlate, 1, shuffledSV));
            cards.AddRange(CreateCards(CardType.Sombrero, 2, shuffledSV));
            cards.AddRange(CreateCards(CardType.TenGallonHat, 1, shuffledSV));
            cards.AddRange(CreateCards(CardType.Hideout, 2, shuffledSV));
            cards.AddRange(CreateCards(CardType.Silver, 1, shuffledSV));
        }

        foreach (var card in cards.OrderBy(_ => _random.Next()))
        {
            _drawPile.Push(card);
        }
    }

    private void ShuffleDeck()
    {
        var cards = _drawPile.ToList();
        _drawPile.Clear();
        foreach (var card in cards.OrderBy(_ => _random.Next()))
        {
            _drawPile.Push(card);
        }
    }

    private void DrawCards(PlayerState player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (_drawPile.Count == 0)
            {
                ReshuffleDiscardIntoDraw();
            }

            if (_drawPile.Count == 0)
            {
                break;
            }

            player.Hand.Add(_drawPile.Pop());
        }
    }

    private void ReshuffleDiscardIntoDraw()
    {
        if (_discardPile.Count == 0)
        {
            return;
        }

        foreach (var card in _discardPile.OrderBy(_ => _random.Next()))
        {
            _drawPile.Push(card);
        }

        _discardPile.Clear();
    }

    private static string FormatCardValue(int value)
    {
        return value switch
        {
            11 => "J",
            12 => "Q",
            13 => "K",
            14 => "A",
            _ => value.ToString()
        };
    }

    private static string FormatCheckCard(Card card)
    {
        var suitSymbol = card.Suit switch
        {
            CardSuit.Spades => "\u2660",
            CardSuit.Hearts => "\u2665",
            CardSuit.Diamonds => "\u2666",
            CardSuit.Clubs => "\u2663",
            _ => "?"
        };
        return $"{FormatCardValue(card.Value)}{suitSymbol}";
    }

    private Card DrawCheckCard()
    {
        if (_drawPile.Count == 0) ReshuffleDiscardIntoDraw();
        if (_drawPile.Count == 0)
        {
            return new Card("Проверка", CardType.Bang, CardCategory.Brown, "", false, null, "", CardSuit.Clubs, 10);
        }
        var card = _drawPile.Pop();
        _discardPile.Add(card);
        // Blessing: all draw checks are Hearts
        if (IsEventActive(EventCardType.Blessing))
        {
            card = card with { Suit = CardSuit.Hearts };
        }
        return card;
    }

    private IEnumerable<Card> CreateCards(CardType type, int count, Queue<(CardSuit Suit, int Value)> suitValues)
    {
        var definition = CardLibrary.Get(type);
        for (var i = 0; i < count; i++)
        {
            var sv = suitValues.Dequeue();
            yield return new Card(
                definition.Name,
                definition.Type,
                definition.Category,
                definition.Description,
                definition.RequiresTarget,
                definition.TargetHint,
                definition.ImagePath,
                sv.Suit,
                sv.Value);
        }
    }
}
