﻿namespace FSharpx.Collections.Experimental

//TODO: This should be better documented

open FSharpx.Collections
open System.Collections.Generic

exception Empty of string //TODO: improve this and include in the exceptions or use the Empty that is already there
exception IncompatibleMerge of string

type private 'T SBHTree = Node of 'T * 'T list * 'T SBHTree list

type private 'T SBHTreeRoot = Root of int * 'T SBHTree

[<AutoOpen>]
module private Helper =
    let inline xor a b = (a && not b) || (not a && b)

module private SBHTree =
    let item (Node (x, _, _)) = x

    let link descending (Node (x, auxX, childrenX) as treeX) (Node (y, auxY, childrenY) as treeY) =
        if xor (x <= y) descending
            then Node (x, auxX, treeY::childrenX)
            else Node (y, auxY, treeX::childrenY)

    let skewLink descending v treeX treeY =
        let (Node (w, aux, children)) = link descending treeX treeY
        if xor (v <= w) descending
            then Node (v, w::aux, children)
            else Node (w, v::aux, children)

module private SBHTreeRoot =
    open SBHTree

    let inline getTree (Root (_, tree)) = tree

    let insert descending value = function
        | Root(rank1, tree1)::Root(rank2, tree2)::roots when rank1 = rank2 -> Root(rank1 + 1, skewLink descending value tree1 tree2)::roots
        | roots                                                            -> Root(0, Node (value, [], []))::roots

    let rec insRoot descending (Root(rank, tree) as root) roots =
        match roots with
        | []                                  -> [root]
        | Root(rank', _)::_ when rank < rank' -> root::roots
        | Root(_, tree')::roots'              -> insRoot descending (Root(rank + 1, link descending tree tree')) roots'

    let rec mergeRoots descending roots1 roots2 =
        match roots1, roots2 with
        | roots, [] | [], roots-> roots
        | Root(rankX, treeX)::roots1', Root(rankY, _)::_ when rankX < rankY -> Root(rankX, treeX)::mergeRoots descending roots1' roots2
        | Root(rankX, _)::_, Root(rankY, treeY)::roots2' when rankX > rankY -> Root(rankY, treeY)::mergeRoots descending roots1 roots2'
        | Root(rank, treeX)::roots1', Root(_, treeY)::roots2'               -> insRoot descending (Root(rank + 1, link descending treeX treeY)) (mergeRoots descending roots1' roots2')

    let normalize descending = function
        | [] -> []
        | root::roots -> insRoot descending root roots

    let rec extractMinRoot descending = function
        | [p] -> (p, [])
        | (Root(_, tree) as root)::roots ->
            let (Root(_, tree') as root', roots') = extractMinRoot descending roots
            if xor (SBHTree.item tree <= SBHTree.item tree') descending
                then root, roots
                else root', root::roots'
        | [] -> failwith "This should never happen"
        
    let rec findMinRootItem descending = function
        | [Root(_, node)]                       -> item node
        | Root(_, node)::roots'                 -> 
            let this = item node
            let other = findMinRootItem descending roots'
            if xor (this <= other) descending
                then this
                else other
        | []                                    -> failwith "This should never happen"

    let rec toListOrdered descending roots =
        let rec treeToList acc = function
            | (Node (x, aux, children)::ts)::tls ->
                let nacc = aux |> List.fold (fun xs x -> x::xs) (x::acc)
                treeToList nacc (children::ts::tls)
            | []::tls -> treeToList acc tls
            | [] -> acc
        let sorted =
            [(roots |> List.map getTree)]
            |> treeToList []
            |> List.sort
        if descending
            then sorted |> List.rev
            else sorted
        
//****************************************************************************************************
//TODO: Implement equality to be able to compare two heaps
//TODO: Maybe implement comparison too?
type 'T SkewBinomialHeap when 'T: comparison private (count, descending, roots: 'T SBHTreeRoot list) = 
        
    new() = SkewBinomialHeap(0, false, [])

    new(descending) = SkewBinomialHeap(0, descending, [])

    member private this.Roots = roots
        
    member this.Count = count

    member this.IsDescending = descending

    member this.IsEmpty = count = 0

    member this.Insert value =
        SkewBinomialHeap (count + 1, descending, SBHTreeRoot.insert descending value roots)

    member this.TryMerge (other: 'T SkewBinomialHeap) =
        if descending = other.IsDescending 
            then Some (SkewBinomialHeap (count + other.Count, descending, SBHTreeRoot.mergeRoots descending (SBHTreeRoot.normalize descending roots) (SBHTreeRoot.normalize descending other.Roots)))
            else None

    member this.Merge (other: 'T SkewBinomialHeap) =
        match this.TryMerge other with
        | Some h -> h
        | _      -> raise (IncompatibleMerge "Can not merge two heaps with diferent comparison methods")

    member this.TryHead () =
        if count = 0
            then None
            else Some (SBHTreeRoot.findMinRootItem descending roots)

    member this.Head () =
        match this.TryHead () with
        | Some h -> h
        | _      -> raise (Empty "Empty heap, no head")

    member this.TryTail () =
        if count = 0 then
            None
        else
            //find the root with the minimum value a return it along with the remaining roots
            let (Root(rank, Node(_, aux, children)), roots') = SBHTreeRoot.extractMinRoot descending roots

            //reverse the children a set their ranks based on the parent's rank
            let (_, reversed) = children |> List.fold (fun (rank, trees) tree -> rank - 1, Root(rank - 1, tree)::trees) (rank, [])

            //merge the reversed children with the remaining trees
            let merged = SBHTreeRoot.mergeRoots descending reversed (SBHTreeRoot.normalize descending roots') 

            //reinsert all "auxiliary" elements
            let newRoots = aux |> List.fold (fun roots value -> SBHTreeRoot.insert descending value roots) merged

            Some (SkewBinomialHeap (count - 1, descending, newRoots))

    member this.Tail () =
        match this.TryTail () with
        | Some t -> t
        | _      -> raise (Empty "Empty heap, no tail")

    member this.TryUncons () =
        this.TryHead() |> Option.map (fun h -> h, this.Tail())

    member this.Uncons () =
        match this.TryUncons () with
        | Some t -> t
        | None   -> raise (Empty "Empty heap, no head and no tail")

    member this.ToList () =
        SBHTreeRoot.toListOrdered descending roots

    interface IEnumerable<'T> with
        member this.GetEnumerator () = (SBHTreeRoot.toListOrdered descending roots |> List.toSeq).GetEnumerator ()
        
        member this.GetEnumerator (): System.Collections.IEnumerator = upcast (this :> _ seq).GetEnumerator ()        

    interface IHeap<'T SkewBinomialHeap, 'T> with
        member this.Count () = this.Count
        
        member this.Head () = this.Head ()
        
        member this.Insert value = this.Insert value
        
        member this.IsDescending = this.IsDescending
        
        member this.IsEmpty = this.IsEmpty
        
        member this.Length () = this.Count
        
        member this.Merge other = this.Merge other
        
        member this.Tail () = this.Tail ()
        
        member this.TryGetHead () = this.TryHead ()
        
        member this.TryGetTail () = this.TryTail ()
        
        member this.TryMerge other = this.TryMerge other
        
        member this.TryUncons () = this.TryUncons ()
        
        member this.Uncons () = this.Uncons ()

    interface IPriorityQueue<'T> with
        
        member this.Insert value = upcast this.Insert value
        
        member this.IsEmpty = this.IsEmpty
        
        member this.Length = this.Count
        
        member this.Peek () = this.Head ()
        
        member this.Pop() = let head, tail = this.Uncons () in head, upcast tail
        
        member this.TryPeek() = this.TryHead ()
        
        member this.TryPop() = this.TryUncons () |> Option.map (fun (h, t) -> h, upcast t)
        
module SkewBinomialHeap =
    
    let inline (|Cons|Nil|) (heap: 'T SkewBinomialHeap) = match heap.TryUncons () with | Some (h, t) -> Cons(h, t) | None -> Nil

    ///O(1) - Returns an empty heap.
    let inline empty descending = SkewBinomialHeap (descending)

    ///O(log n) - Returns the element at the front. Throws if empty.
    let inline head (xs: 'T SkewBinomialHeap)  = xs.Head ()

    ///O(log n) - Returns Some x where x is the element at the front.
    ///Returns None if the collection is empty.
    let inline tryHead (xs: 'T SkewBinomialHeap)  = xs.TryHead ()

    ///O(1) - Returns a new heap with the element inserted.
    let inline insert x (xs: 'T SkewBinomialHeap) = xs.Insert x

    ///O(1) - Returns true if the heap has no elements.
    let inline isEmpty (xs: 'T SkewBinomialHeap) = xs.IsEmpty

    ///O(1) - Returns true if a call to head of tryHead would return the maximum element in the collection.
    /// Returns false if the element at the head is the minimum.
    let inline isDescending (xs: 'T SkewBinomialHeap) = xs.IsDescending

    ///O(1) - Returns the number of elements in the collection.
    let inline length (xs: 'T SkewBinomialHeap) = xs.Count

    ///O(1) - Returns the number of elements in the collection.
    let inline count (xs: 'T SkewBinomialHeap) = xs.Count

    ///O(log n) - Returns a new heap with the elements of both heaps. The two heaps must have the same isDescending value.
    let inline merge (xs: 'T SkewBinomialHeap) (ys: 'T SkewBinomialHeap) = xs.Merge ys

    ///O(log n) - Returns Some h where h is the merged heap, is both original heaps have the same isDescending value.
    /// Returns None if isDescending is diferent in the heaps supplied.
    let inline tryMerge (xs: 'T SkewBinomialHeap) (ys: 'T SkewBinomialHeap) = xs.TryMerge ys

    ///O(n) - Returns heap from the sequence.
    let ofSeq descending s = s |> Seq.fold (fun heap value -> insert value heap) (empty descending)

    ///O(log n) - Returns a new heap with the front (head) element removed. Throws if empty.
    let inline tail (xs: 'T SkewBinomialHeap) = xs.Tail ()

    ///O(log n) - Returns Some h where h is the heap with the front (head) element removed.
    /// Returns None if the collection is empty.
    let inline tryTail (xs: 'T SkewBinomialHeap) = xs.TryTail()

    /// O(log n) - Returns the head element and tail. Throws if empty.
    let inline uncons (xs: 'T SkewBinomialHeap) = xs.Uncons()

    /// O(log n) - Returns Some (h, t) where h is the head and t is the tail.
    /// Returns None if the collection is empty.
    let inline tryUncons (xs: 'T SkewBinomialHeap) = xs.TryUncons()

    /// O(n * log n) - Returns and ordered sequence of the elements in the heap.
    let inline toSeq (xs: 'T SkewBinomialHeap) = xs :> 'T IEnumerable

    /// O(n * log n) - Returns and ordered list of the elements in the heap.
    let inline toList (xs: 'T SkewBinomialHeap) = xs.ToList()