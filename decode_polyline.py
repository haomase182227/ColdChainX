import sys

def decode_polyline(polyline_str):
    index = 0
    lat = 0
    lng = 0
    coordinates = []
    
    while index < len(polyline_str):
        shift = 0
        result = 0
        while True:
            byte = ord(polyline_str[index]) - 63
            index += 1
            result |= (byte & 0x1f) << shift
            shift += 5
            if byte < 0x20:
                break
        
        latitude_change = ~(result >> 1) if (result & 1) else (result >> 1)
        shift = 0
        result = 0
        
        while True:
            byte = ord(polyline_str[index]) - 63
            index += 1
            result |= (byte & 0x1f) << shift
            shift += 5
            if byte < 0x20:
                break
        
        longitude_change = ~(result >> 1) if (result & 1) else (result >> 1)
        
        lat += latitude_change
        lng += longitude_change
        
        coordinates.append((lat / 1e5, lng / 1e5))
        
    return coordinates

polyline_str = r"""uau`Aab_jSPEf@M`ASbASn@MjAWhDo@vAWh@Kh@ITE~AYRCl@KPErB]zBa@bBY\GvDq@l@PZlCj@bFdA`KFf@b@AVAZCpAYlCk@NCTEnDa@|Eg@@@@@B@DBD?B@D?DADABCBABE@C@C@C?EbBLd@DdAHnAL~ANb@lBJ^d@`BVz@r@jBXr@rBnE|A`DZl@Vd@NXXh@b@x@l@lAzBvEdCfFNZN\Tb@~AdDf@bAVd@dDtGdArB|CfGVf@LTTb@xDtHnEvIhAxBNXd@|\r@~A~CbEzHjFlKNNR^R^b@f@jLhU|@pBrDdIRd@dD|GtBxEpBnEpCtGxIrR~A|Dx@vBDHBFJXbErKlC`HnB|ErGvP`AhCnA`DHPfN~]b@fAlF`NHTt@jB`@dA~B`GdC|GDf@?|@G\GPKLMHOFYBU?WESKOQKSEU?WFWR]NKZMd@Il@IxD^nFf@~Fl@bBPxCXv@Hl@BR@|CHn@DdHp@jAL`AJnCVvCXpCd@nAZnC|@hAXxANzA@rA@bCCfIOnBAjB@vAHzFf@~B\~@Rv@PrNzCp@NlH~AHLNJXJb@JlCp@pAZbCj@?D@D??@D@BBD@BBDDBFDDBFBZh@N`@Jj@RtAXnBTrB`AzHtCfWr@hGl@zEnAlKpFxe@fFze@TnBX`Cv@pGFd@lA~H|ElXlB|Kr@vEnAdHdA|Ef@`CnAfFhAbE`BlFRl@|B~GdHpRzG`RlAhDdGjP`Ybw@Zz@fGrPtAfEvBzGnDxMnApFh@pCrBlL~BhPnE~\pKfv@tArIrBhLfBnItBrJ~DbQbGlVbDnNtBrJdBrHnArEhAvDx@fCjAdDlCpG~BjF|AdDxAlC|CfF@@fEdH~DxFhBjCdAfBdDzEfGrInBhC|GtJnNpStNlTbGtItRnYdNpRvOjRdYhZ|J|Jrd@be@hBhBp@p@bPhPfF~EhIrHdCzBjYlXrFrFzSbTnLpL|FbGfJlJn@n@bL`LzDnDrP|MhR`ObKbIhCvBrBlBlCnCfGrGvb@zc@nPtP??rMnK`EbDtm@ve@lXfTzi@jc@lPxMhKbIzP~MdVpQ`Ar@`EvChFfDnJbFnLjF~YjM~_@pPJDlD`BzBlAvGfDrk@v\|A~@nQlKdX~NvQtJpCzApMbHhEjBtFnBpD~@~KlCdnBzc@bAT|NhDdFlAfDz@zH`CvIxCpEzAbUnHxFnBnC|@~RvGhBj@~IvCfLxDjCz@~@ZpDpAtFzBfCjAjG`DxA|@`@VxI~FzMjJbFjD`@XvDjCzJfHpBjB|DhE`CzCbCpDf@~@vCrFhApC`@|@jAhD`@~AbBrILz@l@jE\~CLzBRhEd@dPj@lRB|@nCv}@TdKLpJElIYtIK`CUhEy@vIYjCcF`_@QxAqAfKS~A_@jDq@|J[lHK|ECrGBjJJvFRxEXzE^fFr@vGxAfKpFt[bFh[Lv@`Ije@nH~b@xDlVbAnGvI|k@zBzRz@jH`ArIvCle@RbEfAzT~@xRdGjpANnCbDxo@XfJLzGNnHBlKH`UKfLQxJ[tSk@hb@qAlu@]vISlFM~CcBd]kArSQrD_B`]Q|EIdIAvJJhJbA`Sr@bLrBr`@dAvS^|IBzHB~GMzQSh\BjPXbLj@jIdA`Kd@lCz@bF|Ipj@nDhTtOjaA`BvMlAnL`AbNVdDnB~[VnDt@|LdAvRXnEzB|_@JvAHdABZdAdMd@hDtArH~ApH^tAxAlFjBlFhCrGpBzD~HtP`Rj_@`DzGt@xA|Tnd@`H~NrTvd@NXrGfNrb@ffAlG`ObJbU`GfNlFlLjAhCnYro@lBjEzWhm@nMp\vCnHvFnNlJdUtAbDbFdLtEjKrAvC`L|VxStd@bAlBbBbDpAdC~CjFvNzSjCrD~U`\jJlMrb@tk@vJdNdKfN`MrP`LjO`BzB`Yx_@hGpIxH~IxBlB`Ap@tBtAp@d@tC`B`EfBhEtAtD`AvF~@xFh@lBHhEBlEK`DU|BSrC[`B[dEw@rC{@vCiAtBy@`o@iW`c@qPrHiDxEeC~E_DrFqEvE}ElGiIdEcG~@uAzJuNbAgApByBrEuDhEkCb@YvEyBtGyBt\eJh]mJrEu@`Ga@~AG`CDhBHb@Bv@HnBVrEv@tHvAdCd@dB^xFrApI~AzaA~QpHxA`IdB~I~BvIhCTHxJlDbC`AhGjCbJdE~GbD`Bv@xyApr@tCtAvOnHnFbCzEvBjGhCzGlCjJjDlL~DlBj@pFbB~ExAzHvBfSjFdBd@`aAzVlGdB|UfGlMlDhGfBtGpBrFjB~KzDtJtDr@X`j@|TvKjEdYjLbE|A|I~CvIvCHB~LtDjHpBbGxAdK`CrMpCrGnApAXdI~A`TjEv~@xQtOfDbOnDxIfCxx@jYp[fLjL~DdStGnQhFbIpBhS|ElFlAd|@fSfYrGhDn@`BT`CTpADxBBtCC~@ClHWb@Ct@C|@ChAEhJa@zEUtBMjS_Aj]}AfG[fCMrHWr@AfDDjCLdE^dANbBZzA^lBl@hAb@`Bp@fB`AbBbAhAz@jA|@z@v@fCnCl@t@fA`Bd@l@jBdCrMdQ`ErF`FxGlKnNjGhIdZna@lK~NfEpFjYz^rP~ThJxLtIfLnCnDnIfLfFzGjV`\bCjDnCzDrA`C^n@x@vARd@d@`AxBhFnEpL^bAzA`EjC|F|@bBxCpFr@bAtDpFxC~DhFjHv@fA|DnFp@jAAFAFAF?B?F@F@FBFBFDDDDDBDBFBF@F@F?F?DA\XZ^|E|Gd@n@vAnBjIdLn@`AfCzD~@xAnItO^x@~BhFdFdM|AtEOHgAn@mErAOBwCf@OD}@NQoBOc@QUc@?gAb@{CnBaA~@iBdB"""

coords = decode_polyline(polyline_str)
print("Start:", coords[0])
print("End:", coords[-1])
