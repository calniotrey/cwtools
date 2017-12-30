namespace CWTools.Process

open CWTools.Parser
open CWTools.Localisation
open Newtonsoft.Json
open System

module List =
  let replace f sub xs = 
    let rec finish acc = function
      | [] -> acc
      | x::xs -> finish (x::acc) xs
    let rec search acc = function
      | [] -> None
      | x::xs -> 
        if f x then Some(finish ((sub x)::xs) acc)
        else search (x::acc) xs
    search [] xs
  let replaceOrAdd f sub add xs =
    let result = replace f sub xs
    match result with
    | Some ls -> ls
    | None -> add::xs
    
type Leaf(keyvalueitem : KeyValueItem) =
    let (KeyValueItem (Key(key), value)) = keyvalueitem
    member val Key = key with get, set
    member val Value = value with get, set
    [<JsonIgnore>]
    member this.ToRaw = KeyValueItem(Key(this.Key), this.Value)
type LeafValue(value : Value) =
    member val Value = value with get, set
    [<JsonIgnore>]
    member this.ToRaw = Value(this.Value)
type Node (key : string) =
    let bothFind x = function |NodeI n when n.Key = x -> true |LeafI l when l.Key = x -> true |_ -> false

    member val Key = key
    member val All : Both list = List.empty with get, set
    member this.Children = this.All |> List.choose (function |NodeI n -> Some n |_ -> None)
    member this.Values = this.All |> List.choose (function |LeafI l -> Some l |_ -> None)
    member this.Comments = this.All |> List.choose (function |CommentI c -> Some c |_ -> None)
    member this.Has x = this.All |> (List.exists (bothFind x))
    member this.Tag x = this.Values |> List.tryPick (function |l when l.Key = x -> Some l.Value |_ -> None)
    member this.TagText x = this.Tag x |> Option.map (fun t -> t.ToString())
    member this.SetTag x v = this.All <- this.All |> List.replaceOrAdd (bothFind x) (fun _ -> v) v
    member this.Child x = this.Children |> List.tryPick (function |c when c.Key = x -> Some c |_ -> None)

    [<JsonIgnore>]
    member this.ToRaw : Statement list = this.All |> List.rev |>
                                         List.map (function 
                                           |NodeI n -> KeyValue(KeyValueItem(Key n.Key, Clause n.ToRaw))
                                           |LeafValueI lv -> lv.ToRaw
                                           |LeafI l -> KeyValue l.ToRaw 
                                           |CommentI c -> (Comment c))
    
and Both = |NodeI of Node | LeafI of Leaf |CommentI of string |LeafValueI of LeafValue


module ProcessCore =

    let processNode< 'T when 'T :> Node > inner (key : string) (sl : Statement list) : Node =
        let node = match key with
                    |"" -> Activator.CreateInstance(typeof<'T>) :?> Node
                    |x -> Activator.CreateInstance(typeof<'T>, x) :?> Node
        sl |> List.iter (fun e -> inner node e) |> ignore
        node

    type BaseProcess (maps : Map< string, ((Node -> Statement -> unit) -> string -> Statement list -> Node) > ) =
        let rec lookup =
            (fun key ->
                match maps.TryFind key with
                |Some t -> t processNodeInner key
                |None -> processNode<Node> processNodeInner key
                ) >> (fun f a -> NodeI (f a)) 
        and processNodeInner (node : Node) statement =
            match statement with
            | KeyValue(KeyValueItem(Key(k) , Clause(sl))) -> node.All <- lookup k sl::node.All
            | KeyValue(kv) -> node.All <- LeafI(Leaf(kv))::node.All
            | Comment(c) -> node.All <- CommentI c::node.All
            | Value(v) -> node.All <- LeafValueI(LeafValue(v))::node.All
        member __.ProcessNode<'T when 'T :> Node >() = processNode<'T> processNodeInner

    let baseMap = [] |> Map.ofList
    let processNodeBasic = BaseProcess(baseMap).ProcessNode()

    let rec foldNode fNode acc (node : Node) :'r =
        let recurse = foldNode fNode
        let newAcc = fNode acc node
        node.Children |> List.fold recurse newAcc

    let foldNode2 fNode fCombine acc (node:Node) =
        let rec loop nodes cont =
            match nodes with
            | (x : Node)::tail ->
                loop x.Children (fun accChildren ->
                    let resNode = fNode x accChildren
                    loop tail (fun accTail ->
                        cont(fCombine resNode accTail) ))
            | [] -> cont acc
        loop [node] id
    
    let rec cata fNode (node:Node) :'r =
        let recurse = cata fNode
        fNode node (node.Children |> List.map recurse)
    //let ck2Map = ["option", processNode<Node>] |> Map.ofList
    //let processNodeCK2 = BaseProcess(ck2Map)

    // let processNodeInnerFact lookup =
    //     (fun (node : Node) statement ->
    //         match statement with
    //         | KeyValue(KeyValueItem(Key(k) , Clause(sl))) -> node.All <- lookup k sl::node.All
    //         | KeyValue(kv) -> node.All <- LeafI(Leaf(kv))::node.All
    //         | Comment(c) -> node.All <- CommentI c::node.All
    //         | Value(v) -> node.All <- LeafValueI(LeafValue(v))::node.All
    //     )

    // let processNodeLookup baseCase (maps : Map< string, (string -> Statement list -> Node)> ) =
    //     (fun key ->
    //         match maps.TryFind key with
    //         |Some t -> t key
    //         |None -> baseCase key
    //        ) >> (fun f a -> NodeI (f a))
    // let processNode< 'T when 'T :> Node > inner (key : string) (sl : Statement list) : Node =
    //     let node = match key with
    //                 |"" -> Activator.CreateInstance(typeof<'T>) :?> Node
    //                 |x -> Activator.CreateInstance(typeof<'T>, x) :?> Node
    //     sl |> List.iter (fun e -> inner node e) |> ignore
    //     node
    //let processBasicNode = processNode (processNodeInnerFact (processNodeLookup processNode<Node> baseMap))
    
     
    // //let rec processNodeLookup (maps : Map< string, (string -> Statement list -> Node)> ) =
    // let maps =
    //     [
    //         "option", processNode<Option>;
    //         "character_event", processNode<Event>;
    //         "province_event", processNode<Event>;
    //         "letter_event", processNode<Event>;
    //     ] |> Map.ofList
    //     (fun key -> 
    //         match maps.TryFind key with
    //         |Some t -> t key
    //         |None -> processNode<Node> key
    //         ) >> (fun f a -> NodeI (f a))

    // and processNodeInner (node : Node) statement =
    //     match statement with
    //         | KeyValue(KeyValueItem(Key(k) , Clause(sl))) -> node.All <- processNodeLookup k sl::node.All
    //         | KeyValue(kv) -> node.All <- LeafI(Leaf(kv))::node.All
    //         | Comment(c) -> node.All <- CommentI c::node.All
    //         | Value(v) -> node.All <- LeafValueI(LeafValue(v))::node.All

    // and processNode< 'T when 'T :> Node > (key : string) (sl : Statement list) : Node =
    //     let node = match key with
    //                 |"" -> Activator.CreateInstance(typeof<'T>) :?> Node
    //                 |x -> Activator.CreateInstance(typeof<'T>, x) :?> Node
    //     sl |> List.iter (fun e -> processNodeInner node e) |> ignore
    //     node

   