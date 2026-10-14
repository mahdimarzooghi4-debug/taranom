// TRN-TECH-127: human-portable, unsigned test-only exact-write receipt.
// It preserves original bytes and does not prove an actor or any right.
export type SupportAction='SUBMIT_UNVERIFIED'|'FOLLOW_UP_UNVERIFIED';
export type SupportWriteBody={
 requestId:string;expectedRevision:number;action:SupportAction;
 memberRef:string;topic:'MEDICAL_SERVICE_COMPLAINT'|'INSURANCE_COVERAGE_DISPUTE';
 unverifiedEvidenceRef:string;claimedSubmitterRef:string;
 originalSubmissionRequestId:string;claimedFollowUpActorRef:string|null;
 unverifiedFollowUpNoteRef:string|null
};
export type PendingSupportWrite={caseRef:string;body:string;command:SupportWriteBody};
const zero='00000000-0000-0000-0000-000000000000';
const keys=['requestId','expectedRevision','action','memberRef','topic',
 'unverifiedEvidenceRef','claimedSubmitterRef','originalSubmissionRequestId',
 'claimedFollowUpActorRef','unverifiedFollowUpNoteRef'];
const record=(v:unknown):v is Record<string,unknown>=>
 !!v&&typeof v==='object'&&!Array.isArray(v);
const synthetic=(v:unknown):v is string=>
 typeof v==='string'&&v.length<=128&&/^test:[A-Za-z0-9_:-]{1,123}$/.test(v);
const uuid=(v:unknown):v is string=>typeof v==='string'&&
 /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(v);
function exactKeys(v:Record<string,unknown>,expected:string[]){
 return Object.keys(v).join('|')===expected.join('|');
}
export function parseSupportCommand(caseRef:string,body:string):PendingSupportWrite|null{
 if(!synthetic(caseRef)||body.length<10||body.length>1400)return null;
 try{
  const v:unknown=JSON.parse(body);
  if(!record(v)||!exactKeys(v,keys)||JSON.stringify(v)!==body||
    !uuid(v.requestId)||!Number.isSafeInteger(v.expectedRevision)||
    (v.expectedRevision as number)<0||
    (v.expectedRevision as number)>=Number.MAX_SAFE_INTEGER||
    !['SUBMIT_UNVERIFIED','FOLLOW_UP_UNVERIFIED'].includes(String(v.action))||
    !['MEDICAL_SERVICE_COMPLAINT','INSURANCE_COVERAGE_DISPUTE'].includes(String(v.topic))||
    !synthetic(v.memberRef)||!synthetic(v.unverifiedEvidenceRef)||
    !synthetic(v.claimedSubmitterRef))return null;
  if(v.action==='SUBMIT_UNVERIFIED'){
   if(v.expectedRevision!==0||v.originalSubmissionRequestId!==zero||
      v.claimedFollowUpActorRef!==null||v.unverifiedFollowUpNoteRef!==null)return null;
  }else if((v.expectedRevision as number)<1||
   !uuid(v.originalSubmissionRequestId)||!synthetic(v.claimedFollowUpActorRef)||
   !synthetic(v.unverifiedFollowUpNoteRef))return null;
  return {caseRef,body,command:v as SupportWriteBody};
 }catch{return null;}
}
export function makeSupportReceipt(input:PendingSupportWrite):string{
 return JSON.stringify({contract:'TRN-TECH-127',syntheticOnly:true,
  caseRef:input.caseRef,originalBody:input.body});
}
export function parseSupportReceipt(raw:string,caseRef:string):PendingSupportWrite|null{
 if(raw.length<2||raw.length>2400)return null;
 try{
  const env:unknown=JSON.parse(raw);
  if(!record(env)||!exactKeys(env,
   ['contract','syntheticOnly','caseRef','originalBody'])||
   env.contract!=='TRN-TECH-127'||env.syntheticOnly!==true||
   env.caseRef!==caseRef||typeof env.originalBody!=='string'||
   JSON.stringify(env)!==raw)return null;
  return parseSupportCommand(caseRef,env.originalBody);
 }catch{return null;}
}
