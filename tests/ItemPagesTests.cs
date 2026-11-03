using System;
using System.Collections.Generic;
using System.Linq;
using AltiumRevisionRetirement;

static class ItemPagesTests
{
    static string G() {return Guid.NewGuid().ToString("D");}
    static void Assert(bool v) {if(!v) throw new Exception("Item paging contract failed");}
    static void Reject(Action a) {try {a();}catch(InvalidOperationException){return;}throw new Exception("Expected paging rejection");}
    public static void Run(Action<string,Action> test)
    {
        test("zero-based pages cover all items",()=>{var ids=new[]{G(),G(),G()};Assert(ItemPages.Read(3,p=>ids.Skip(p*2).Take(2).ToList()).SequenceEqual(ids));});
        test("first-page alias is accepted once",()=>{var ids=new[]{G(),G(),G()};Assert(ItemPages.Read(3,p=>ids.Skip(Math.Max(0,p-1)*2).Take(2).ToList()).SequenceEqual(ids));});
        test("empty page zero permits one-based first page",()=>{string id=G();Assert(ItemPages.Read(1,p=>p==1 ? new[]{id}:new string[0]).Single()==id);});
        test("empty count still verifies a native page",()=>{int calls=0;Assert(ItemPages.Read(0,p=>{calls++;return new string[0];}).Count==0&&calls==1);});
        test("zero count cannot hide page records",()=>Reject(()=>ItemPages.Read(0,p=>new[]{G()})));
        test("truncated pages never become a successful inventory",()=>{string id=G();Reject(()=>ItemPages.Read(2,p=>p==0 ? new[]{id}:new string[0]));});
        test("repeated pages stop instead of looping",()=>{string id=G();int calls=0;Reject(()=>ItemPages.Read(2,p=>{calls++;return new[]{id};}));Assert(calls==3);});
        test("duplicate rows within a page are rejected",()=>{string id=G();Reject(()=>ItemPages.Read(2,p=>new[]{id,id}));});
        test("count growth during paging blocks inventory",()=>Reject(()=>ItemPages.Read(1,p=>new[]{G(),G()})));
    }
}
